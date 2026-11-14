(*
   JsonDocument — an F* model of Fuaran.Core's lossless document model (`JDoc`, fuaran-core
   Phase 640): its reader over the scanner's token surface, proved to rebuild the strict parse, and
   its conversion to the decode layer's value, proved to carry every sound decoder over to the
   document.

   WHAT IS MODELLED.
     - `src/Fuaran.Core.Wire/Document.fs` — the `JDoc` type (section 1, parametric in the carrier of
       a string and of a number text); `JDoc.reading` / `JDoc.read`, the lossless reading over
       `Reader`'s token surface (section 2): a `null` read by `Reader.readNull`, a number by
       `Reader.numberText` (the scanner's token scan and the grammar, and NO reading of a value), a
       container by the scanner's own member and item loops, a string or a literal by the scanner's
       own arms; `JDoc.toJVal`'s number arm, which reads a number text by the scanner's own
       classification (`Json.readNumberToken`, `classify_number` in `JsonParse.fst`), and the
       conversion as a whole (section 3, over the parser's value; section 5, over the decode
       layer's, with its refusals at their paths); `JDocDecoder.ofDecoder` (section 5); and
       `JDocDecoder` itself (section 6, Phase 658): the decode algebra `DecodeLayer.fst` states
       once over a carrier view, instantiated at the document's carrier as production
       instantiates `DecodeOver`, with the readers the document owns (`nullable`, `bool`,
       `numberText`, `int`, `float`) and the compositions it spells itself.
     - `Json.isFloatToken` — the flag the conversion recomputes from a number text, proved to be the
       flag the scanner records while it scans the token (section 3).

   WHAT IS PROVED, AND WHAT IS NOT. The ladder states both, once: the rows of `../proofs.json`
   whose `evidence.model` is this file, each with its `statement` and its `boundary`. `README.md`
   renders them in this model's section. In one sentence each:
     - `lossless_reader_rebuilds_the_parse` — reading a document losslessly and converting it IS the
       strict parse: on every input, under every limits record, byte verdict and depth budget, the
       strict limited parse accepts exactly the documents whose lossless reading converts, with the
       value the conversion answers; so a document the lossless reader refuses is one the strict
       parse refuses (`lossless_refusal_is_a_parse_refusal`).
     - `of_decoder_is_sound` — a decoder over `JVal` reaches a document through the conversion,
       and every decoder `DecodeLayer` proves sound refuses a document only at a path that
       resolves IN THE DOCUMENT (or names a member a resolving object of it lacks), the
       conversion's own refusals (a null, a number past the carrier) included.
     - `document_refusal_paths_resolve` and one `jd_*_sound` lemma per `JDocDecoder` block — the
       document decoder's OWN combinators, the null reader and the number readers among them, are
       sound by the same law at the same paths as `Decoder`'s: each is `DecodeLayer`'s generic
       lemma at the document's carrier (`dcarrier`), through the carrier's faithfulness
       (`d_ok_at_is_over`), or proved here for a reader the document owns.

   WHAT IS NOT MODELLED. The writer (`JDoc.tryWrite`) and `JDoc.ofJVal`, which the round-trip
   tests hold. The breach's path and the re-scan after a shape mismatch, as in `JsonParse.fst`
   section 14. `JDocDecoder.read`, which is `JDoc.read` then a decoder. The production
   differential "JDocDecoder is Decoder's algebra" (`JsonDocumentTests.fs`) stays, as the check
   that both production instances are the one code the model describes.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it. The parser's
   alphabet, its scans and its messages are `JsonParse`'s, opened under `P`; the decode layer's
   paths, codes and decoders are `DecodeLayer`'s under `D`, over `WireDecode`'s value under `W`.

   Apache-2.0, like everything beside it.
*)
module JsonDocument

open Twin
open ListAlgebra

module P = JsonParse
module W = WireDecode
module D = DecodeLayer

(* ======================================================================================
   1. The document (F#: `JDoc`), parametric in the carrier of a string `s` and of a number's
      text `t`. Section 2 reads it with the parser's carriers; section 5 lowers it with the
      decode layer's.
   ====================================================================================== *)

type jdoc (s t: eqtype) =
  | DNull : jdoc s t
  | DBool : b:bool -> jdoc s t
  | DNum  : n:t -> jdoc s t
  | DStr  : str:s -> jdoc s t
  | DArr  : items:list (jdoc s t) -> jdoc s t
  | DObj  : fields:list (s & jdoc s t) -> jdoc s t

(* The document the reader builds: a string as the parser decodes it, a number as its token. *)
let pdoc = jdoc (list P.och) (list P.ch)

(* F#: the member-name check of the reader's object loop, over the document's members — the
   scanner's `objectLoop` refuses a repeated name whatever value it carries. *)
let rec has_dkey (k: list P.och) (fs: list (list P.och & pdoc)) : Tot bool (decreases fs) =
  match fs with
  | [] -> false
  | (k', _) :: t -> k = k' || has_dkey k t

(* ======================================================================================
   2. THE LOSSLESS READING (F#: `JDoc.reading`, run by `JDoc.read` through `Reader.read`) — the
      strict parse's descent with its two JVal-only arms replaced: a `null` is read on the
      document's own rule (`Reader.readNull`), and a number is read as its TEXT
      (`Reader.numberText`: the token scan and the grammar, and no classification). Every other
      arm, every bound, every loop and every refusal is the parser's, with the parser's
      refinements.
   ====================================================================================== *)

let rec lossless_value
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit) (s: list P.ch)
  : Tot (r: P.pres pdoc { (P.POk? r ==> P.llen (P.POk?.rest r) < P.llen s) /\ P.depth_confined r /\
                          P.bounds_confined lim r })
        (decreases %[P.llen s; 2])
  =
  let w = P.skip_ws s in
  match w with
  | [] -> P.PErr P.UnexpectedEndOfInput "unexpected end of input" []
  | P.CQuote :: _ ->
    (match P.parse_string lim.P.lstr w with
     | P.PErr k m a -> P.PErr k m a
     | P.POk cs r -> P.POk (DStr cs) r)
  | P.CLBrace :: _ -> lossless_object fr cap lim b w
  | P.CLBrack :: _ -> lossless_array fr cap lim b w
  | P.CLt :: P.CLr :: P.CLu :: P.CLe :: t -> P.POk (DBool true) t
  | P.CLt :: _ -> P.PErr P.ExpectedToken (P.msg_expect_lit "true") w
  | P.CLf :: P.CLa :: P.CLl :: P.CLs :: P.CLe :: t -> P.POk (DBool false) t
  | P.CLf :: _ -> P.PErr P.ExpectedToken (P.msg_expect_lit "false") w
  (* F#: `Reader.readNull` — the literal consumed, no policy consulted; a near miss is the
     literal's own refusal. *)
  | P.CLn :: P.CLu :: P.CLl :: P.CLl :: t -> P.POk DNull t
  | P.CLn :: _ -> P.PErr P.ExpectedToken (P.msg_expect_lit "null") w
  | c :: _ ->
    if c = P.CMinus || P.is_digit c then
      (* F#: `Reader.numberText` — `scanNumberText`: the token, held to the grammar, read as no
         value. The refusal is the parser's own, at the end of the token. *)
      (let (tok, _, rest) = P.scan_number w in
       if P.is_json_number tok then P.POk (DNum tok) rest
       else P.PErr P.MalformedNumber (P.msg_malformed tok) rest)
    else P.PErr P.UnexpectedChar (P.msg_unexpected_char c) w

and lossless_object
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (w: list P.ch { P.starts_container w })
  : Tot (r: P.pres pdoc { (P.POk? r ==> P.llen (P.POk?.rest r) < P.llen w) /\ P.depth_confined r /\
                          P.bounds_confined lim r })
        (decreases %[P.llen w; 1])
  =
  match b with
  | [] -> P.PErr P.MaxDepthExceeded (P.msg_depth cap) w
  | _ :: b' ->
    (match P.expect P.CLBrace w with
     | P.PErr k m a -> P.PErr k m a
     | P.POk _ t ->
       let t1 = P.skip_ws t in
       (match t1 with
        | P.CRBrace :: t2 -> P.POk (DObj []) t2
        | _ ->
          (match lossless_members fr cap lim b' [] t1 with
           | P.PErr k m a -> P.PErr k m a
           | P.POk fs r -> P.POk (DObj fs) r)))

and lossless_array
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (w: list P.ch { P.starts_container w })
  : Tot (r: P.pres pdoc { (P.POk? r ==> P.llen (P.POk?.rest r) < P.llen w) /\ P.depth_confined r /\
                          P.bounds_confined lim r })
        (decreases %[P.llen w; 1])
  =
  match b with
  | [] -> P.PErr P.MaxDepthExceeded (P.msg_depth cap) w
  | _ :: b' ->
    (match P.expect P.CLBrack w with
     | P.PErr k m a -> P.PErr k m a
     | P.POk _ t ->
       let t1 = P.skip_ws t in
       (match t1 with
        | P.CRBrack :: t2 -> P.POk (DArr []) t2
        | _ ->
          (match lossless_items fr cap lim b' [] t1 with
           | P.PErr k m a -> P.PErr k m a
           | P.POk xs r -> P.POk (DArr xs) r)))

(* F#: `Reader.members` — `objectLoop` under `RejectNull` (nothing erased), the reading of each
   value the lossless one. *)
and lossless_members
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (acc: list (list P.och & pdoc)) (s: list P.ch)
  : Tot (r: P.pres (list (list P.och & pdoc)) { (P.POk? r ==> P.llen (P.POk?.rest r) <= P.llen s) /\
                                                P.depth_confined r /\ P.bounds_confined lim r })
        (decreases %[P.llen s; 0])
  =
  let w = P.skip_ws s in
  if P.spent lim.P.lmembers acc then P.PErr (P.LimitExceeded P.LMembers) P.msg_limit w
  else
  match P.parse_string lim.P.lstr w with
  | P.PErr k m a -> P.PErr k m a
  | P.POk key t ->
    let t1 = P.skip_ws t in
    (match P.expect P.CColon t1 with
     | P.PErr k m a -> P.PErr k m a
     | P.POk _ t2 ->
       let t3 = P.skip_ws t2 in
       (match lossless_value fr cap lim b t3 with
        | P.PErr k m a -> P.PErr k m a
        | P.POk v t4 ->
          if has_dkey key acc then P.PErr P.DuplicateMember P.msg_duplicate w
          else
            let t5 = P.skip_ws t4 in
            (match t5 with
             | P.CComma :: t6 -> lossless_members fr cap lim b ((key, v) :: acc) t6
             | P.CRBrace :: t6 -> P.POk (P.rev ((key, v) :: acc)) t6
             | _ -> P.PErr P.ExpectedToken "expected ',' or '}'" t5)))

(* F#: `Reader.items` — `arrayLoop`, the reading of each item the lossless one. *)
and lossless_items
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (acc: list pdoc) (s: list P.ch)
  : Tot (r: P.pres (list pdoc) { (P.POk? r ==> P.llen (P.POk?.rest r) <= P.llen s) /\
                                 P.depth_confined r /\ P.bounds_confined lim r })
        (decreases %[P.llen s; 3])
  =
  if P.spent lim.P.litems acc then P.PErr (P.LimitExceeded P.LItems) P.msg_limit s
  else
  match lossless_value fr cap lim b s with
  | P.PErr k m a -> P.PErr k m a
  | P.POk v t ->
    let t1 = P.skip_ws t in
    (match t1 with
     | P.CComma :: t2 -> lossless_items fr cap lim b (v :: acc) t2
     | P.CRBrack :: t2 -> P.POk (P.rev (v :: acc)) t2
     | _ -> P.PErr P.ExpectedToken "expected ',' or ']'" t1)

(* F#: `JDoc.read`'s answer — the document, or the refusal `Reader.read` raises, every breach
   lifted as `JsonParse.parse_limited` lifts it. *)
type dresult =
  | DOk    : d:pdoc -> dresult
  | DErr   : k:P.ekind -> msg:string -> at:list P.ch -> dresult
  | DLimit : l:P.limit -> at:list P.ch -> dresult

(* F#: `JDoc.read` — the byte verdict, the lossless reading, the trailing-input test. *)
let read_lossless
  (fr: list P.ch -> P.freadv) (cap: string) (b: list unit) (lim: P.lims) (bytes_ok: bool)
  (s: list P.ch)
  : Tot dresult =
  if not bytes_ok then DLimit P.LBytes s
  else
    match lossless_value fr cap lim b s with
    | P.PErr P.MaxDepthExceeded _ a -> DLimit P.LDepth a
    | P.PErr (P.LimitExceeded l) _ a -> DLimit l a
    | P.PErr k m a -> DErr k m a
    | P.POk d r ->
      let r1 = P.skip_ws r in
      if Nil? r1 then DOk d else DErr P.TrailingCharacters "trailing characters" r1

(* ======================================================================================
   3. The conversion to the parser's value (F#: `JDoc.toJVal`, its refusal's code and path
      dropped here — section 5 keeps them), and the float flag it recomputes.
   ====================================================================================== *)

(* F#: `Json.isFloatToken` — a point or an exponent marker anywhere in the token. *)
let rec is_float_tok (tok: list P.ch) : Tot bool (decreases tok) =
  match tok with
  | [] -> false
  | c :: t -> c = P.CDot || P.is_exp c || is_float_tok t

let rec to_jval (fr: list P.ch -> P.freadv) (d: pdoc) : Tot (option P.jval) (decreases d) =
  match d with
  | DNull -> None
  | DBool b -> Some (P.JBool b)
  | DStr cs -> Some (P.JStr cs)
  | DNum tok ->
    (match P.classify_number fr tok (is_float_tok tok) with
     | P.POk v _ -> Some v
     | P.PErr _ _ _ -> None)
  | DArr xs ->
    (match to_jvals fr xs with
     | Some ys -> Some (P.JArr ys)
     | None -> None)
  | DObj fs ->
    (match to_fields fr fs with
     | Some gs -> Some (P.JObj gs)
     | None -> None)

and to_jvals (fr: list P.ch -> P.freadv) (xs: list pdoc) : Tot (option (list P.jval)) (decreases xs) =
  match xs with
  | [] -> Some []
  | x :: t ->
    (match to_jval fr x, to_jvals fr t with
     | Some y, Some ys -> Some (y :: ys)
     | _, _ -> None)

and to_fields (fr: list P.ch -> P.freadv) (fs: list (list P.och & pdoc))
  : Tot (option (list (list P.och & P.jval))) (decreases fs) =
  match fs with
  | [] -> Some []
  | (k, x) :: t ->
    (match to_jval fr x, to_fields fr t with
     | Some y, Some ys -> Some ((k, y) :: ys)
     | _, _ -> None)

(* ---- the float flag: what the scanner records IS what the token says ---- *)

[@@ noextract_to "FSharp"]
let rec is_float_app (a b: list P.ch)
  : Lemma (ensures is_float_tok (app a b) == (is_float_tok a || is_float_tok b)) (decreases a) =
  match a with
  | [] -> ()
  | _ :: t -> is_float_app t b

[@@ noextract_to "FSharp"]
let rec digits_are_not_float (s: list P.ch)
  : Lemma (ensures is_float_tok (fst (P.take_digits s)) == false) (decreases s) =
  match s with
  | d :: t -> if P.is_digit d then digits_are_not_float t else ()
  | [] -> ()

[@@ noextract_to "FSharp"]
let scan_number_flag (s: list P.ch)
  : Lemma (ensures (let (tok, isf, _) = P.scan_number s in isf == is_float_tok tok)) =
  let (neg, s1) = (match s with P.CMinus :: t -> ([P.CMinus], t) | _ -> ([], s)) in
  let (ints, s2) = P.take_digits s1 in
  digits_are_not_float s1;
  let (frac, isf1, s3) =
    (match s2 with
     | P.CDot :: t -> let (fs, r) = P.take_digits t in (P.CDot :: fs, true, r)
     | _ -> ([], false, s2)) in
  let (expo, isf2, s4) =
    (match s3 with
     | e :: t ->
       if P.is_exp e then
         (match t with
          | sg :: u ->
            if sg = P.CPlus || sg = P.CMinus then
              let (ds, r) = P.take_digits u in (e :: sg :: ds, true, r)
            else let (ds, r) = P.take_digits t in (e :: ds, true, r)
          | [] -> ([e], true, []))
       else ([], false, s3)
     | [] -> ([], false, s3)) in
  (match s2 with
   | P.CDot :: t -> digits_are_not_float t
   | _ -> ());
  is_float_app frac expo;
  is_float_app ints (app frac expo);
  is_float_app neg (app ints (app frac expo))

(* ======================================================================================
   4. THEOREM — the lossless reading, then the conversion, IS the strict parse.

      `conv` is what a lossless result says once converted: the value and the rest where the
      reading succeeded AND its document converts, nothing otherwise; `ok_of` is what the strict
      result says: the value and the rest where it succeeded. The lemmas, one per member of the
      group in the group's own termination order, say the two are EQUAL — so the strict parse
      accepts exactly where the lossless reading accepts with a document that converts, with the
      converted value and the same rest. The two accumulator facts (`to_fields` of a reversed
      accumulator, and that an accumulator holding a value that does not convert poisons the
      result) are what carry it through the loops.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let conv (fr: list P.ch -> P.freadv) (r: P.pres pdoc) : Tot (option (P.jval & list P.ch)) =
  match r with
  | P.POk d rest -> (match to_jval fr d with Some v -> Some (v, rest) | None -> None)
  | P.PErr _ _ _ -> None

[@@ noextract_to "FSharp"]
let ok_of (#a: Type) (r: P.pres a) : Tot (option (a & list P.ch)) =
  match r with
  | P.POk v rest -> Some (v, rest)
  | P.PErr _ _ _ -> None

[@@ noextract_to "FSharp"]
let conv_fields (fr: list P.ch -> P.freadv) (r: P.pres (list (list P.och & pdoc)))
  : Tot (option (list (list P.och & P.jval) & list P.ch)) =
  match r with
  | P.POk fs rest -> (match to_fields fr fs with Some gs -> Some (gs, rest) | None -> None)
  | P.PErr _ _ _ -> None

[@@ noextract_to "FSharp"]
let conv_items (fr: list P.ch -> P.freadv) (r: P.pres (list pdoc))
  : Tot (option (list P.jval & list P.ch)) =
  match r with
  | P.POk xs rest -> (match to_jvals fr xs with Some ys -> Some (ys, rest) | None -> None)
  | P.PErr _ _ _ -> None

(* ---- the accumulators ---- *)

[@@ noextract_to "FSharp"]
let rec rev_fields (fr: list P.ch -> P.freadv) (l acc: list (list P.och & pdoc))
  : Lemma (ensures to_fields fr (P.rev_app l acc) ==
                   (match to_fields fr l, to_fields fr acc with
                    | Some a, Some b -> Some (P.rev_app a b)
                    | _, _ -> None))
          (decreases l) =
  match l with
  | [] -> ()
  | x :: t -> rev_fields fr t (x :: acc)

[@@ noextract_to "FSharp"]
let rec rev_items (fr: list P.ch -> P.freadv) (l acc: list pdoc)
  : Lemma (ensures to_jvals fr (P.rev_app l acc) ==
                   (match to_jvals fr l, to_jvals fr acc with
                    | Some a, Some b -> Some (P.rev_app a b)
                    | _, _ -> None))
          (decreases l) =
  match l with
  | [] -> ()
  | x :: t -> rev_items fr t (x :: acc)

[@@ noextract_to "FSharp"]
let rec fields_len_keys (fr: list P.ch -> P.freadv) (k: list P.och)
  (l: list (list P.och & pdoc)) (m: list (list P.och & P.jval))
  : Lemma (requires to_fields fr l == Some m)
          (ensures P.llen l == P.llen m /\ has_dkey k l == P.has_key k m)
          (decreases l) =
  match l, m with
  | _ :: t, _ :: u -> fields_len_keys fr k t u
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec items_len (fr: list P.ch -> P.freadv) (l: list pdoc) (m: list P.jval)
  : Lemma (requires to_jvals fr l == Some m) (ensures P.llen l == P.llen m) (decreases l) =
  match l, m with
  | _ :: t, _ :: u -> items_len fr t u
  | _ -> ()

(* An accumulator holding a member that does not convert: whatever the loop goes on to read, its
   document does not convert. *)
[@@ noextract_to "FSharp"]
let rec poisoned_members
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (acc: list (list P.och & pdoc)) (s: list P.ch)
  : Lemma (requires None? (to_fields fr acc))
          (ensures None? (conv_fields fr (lossless_members fr cap lim b acc s)))
          (decreases %[P.llen s]) =
  let w = P.skip_ws s in
  if P.spent lim.P.lmembers acc then ()
  else
  match P.parse_string lim.P.lstr w with
  | P.PErr _ _ _ -> ()
  | P.POk key t ->
    let t1 = P.skip_ws t in
    (match P.expect P.CColon t1 with
     | P.PErr _ _ _ -> ()
     | P.POk _ t2 ->
       let t3 = P.skip_ws t2 in
       (match lossless_value fr cap lim b t3 with
        | P.PErr _ _ _ -> ()
        | P.POk v t4 ->
          if has_dkey key acc then ()
          else
            let t5 = P.skip_ws t4 in
            (match t5 with
             | P.CComma :: t6 -> poisoned_members fr cap lim b ((key, v) :: acc) t6
             | P.CRBrace :: _ -> rev_fields fr ((key, v) :: acc) []
             | _ -> ())))

[@@ noextract_to "FSharp"]
let rec poisoned_items
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (acc: list pdoc) (s: list P.ch)
  : Lemma (requires None? (to_jvals fr acc))
          (ensures None? (conv_items fr (lossless_items fr cap lim b acc s)))
          (decreases %[P.llen s]) =
  if P.spent lim.P.litems acc then ()
  else
  match lossless_value fr cap lim b s with
  | P.PErr _ _ _ -> ()
  | P.POk v t ->
    let t1 = P.skip_ws t in
    (match t1 with
     | P.CComma :: t2 -> poisoned_items fr cap lim b (v :: acc) t2
     | P.CRBrack :: _ -> rev_items fr (v :: acc) []
     | _ -> ())

(* ---- the group ---- *)

[@@ noextract_to "FSharp"]
let rec lossless_is_parse_value
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit) (s: list P.ch)
  : Lemma (ensures conv fr (lossless_value fr cap lim b s) == ok_of (P.parse_value fr cap lim false b s))
          (decreases %[P.llen s; 2]) =
  let w = P.skip_ws s in
  match w with
  | P.CLBrace :: _ -> lossless_is_parse_object fr cap lim b w
  | P.CLBrack :: _ -> lossless_is_parse_array fr cap lim b w
  | c :: _ -> if c = P.CMinus || P.is_digit c then scan_number_flag w else ()
  | _ -> ()

and lossless_is_parse_object
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (w: list P.ch { P.starts_container w })
  : Lemma (ensures conv fr (lossless_object fr cap lim b w) == ok_of (P.parse_object fr cap lim false b w))
          (decreases %[P.llen w; 1]) =
  match b with
  | [] -> ()
  | _ :: b' ->
    (match P.expect P.CLBrace w with
     | P.PErr _ _ _ -> ()
     | P.POk _ t ->
       let t1 = P.skip_ws t in
       (match t1 with
        | P.CRBrace :: _ -> ()
        | _ -> lossless_is_parse_members fr cap lim b' [] [] t1))

and lossless_is_parse_array
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (w: list P.ch { P.starts_container w })
  : Lemma (ensures conv fr (lossless_array fr cap lim b w) == ok_of (P.parse_array fr cap lim false b w))
          (decreases %[P.llen w; 1]) =
  match b with
  | [] -> ()
  | _ :: b' ->
    (match P.expect P.CLBrack w with
     | P.PErr _ _ _ -> ()
     | P.POk _ t ->
       let t1 = P.skip_ws t in
       (match t1 with
        | P.CRBrack :: _ -> ()
        | _ -> lossless_is_parse_items fr cap lim b' [] [] t1))

and lossless_is_parse_members
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (lacc: list (list P.och & pdoc)) (racc: list (list P.och & P.jval)) (s: list P.ch)
  : Lemma (requires to_fields fr lacc == Some racc)
          (ensures conv_fields fr (lossless_members fr cap lim b lacc s) ==
                   ok_of (P.parse_members fr cap lim false b racc s))
          (decreases %[P.llen s; 0]) =
  let w = P.skip_ws s in
  fields_len_keys fr [] lacc racc;
  if P.spent lim.P.lmembers lacc then ()
  else
  match P.parse_string lim.P.lstr w with
  | P.PErr _ _ _ -> ()
  | P.POk key t ->
    fields_len_keys fr key lacc racc;
    let t1 = P.skip_ws t in
    (match P.expect P.CColon t1 with
     | P.PErr _ _ _ -> ()
     | P.POk _ t2 ->
       let t3 = P.skip_ws t2 in
       lossless_is_parse_value fr cap lim b t3;
       (match lossless_value fr cap lim b t3 with
        | P.PErr _ _ _ -> ()
        | P.POk v t4 ->
          if has_dkey key lacc then ()
          else
            let t5 = P.skip_ws t4 in
            (match to_jval fr v with
             | None ->
               (match t5 with
                | P.CComma :: t6 -> poisoned_members fr cap lim b ((key, v) :: lacc) t6
                | P.CRBrace :: _ -> rev_fields fr ((key, v) :: lacc) []
                | _ -> ())
             | Some v' ->
               (match t5 with
                | P.CComma :: t6 -> lossless_is_parse_members fr cap lim b ((key, v) :: lacc) ((key, v') :: racc) t6
                | P.CRBrace :: _ -> rev_fields fr ((key, v) :: lacc) []
                | _ -> ()))))

and lossless_is_parse_items
  (fr: list P.ch -> P.freadv) (cap: string) (lim: P.lims) (b: list unit)
  (lacc: list pdoc) (racc: list P.jval) (s: list P.ch)
  : Lemma (requires to_jvals fr lacc == Some racc)
          (ensures conv_items fr (lossless_items fr cap lim b lacc s) ==
                   ok_of (P.parse_items fr cap lim false b racc s))
          (decreases %[P.llen s; 3]) =
  items_len fr lacc racc;
  if P.spent lim.P.litems lacc then ()
  else (
    lossless_is_parse_value fr cap lim b s;
    match lossless_value fr cap lim b s with
    | P.PErr _ _ _ -> ()
    | P.POk v t ->
      let t1 = P.skip_ws t in
      (match to_jval fr v with
       | None ->
         (match t1 with
          | P.CComma :: t2 -> poisoned_items fr cap lim b (v :: lacc) t2
          | P.CRBrack :: _ -> rev_items fr (v :: lacc) []
          | _ -> ())
       | Some v' ->
         (match t1 with
          | P.CComma :: t2 -> lossless_is_parse_items fr cap lim b (v :: lacc) (v' :: racc) t2
          | P.CRBrack :: _ -> rev_items fr (v :: lacc) []
          | _ -> ())))

(* F#: what `JDoc.read` then `JDoc.toJVal` answer, as a value or nothing. *)
let converted (fr: list P.ch -> P.freadv) (r: dresult) : Tot (option P.jval) =
  match r with
  | DOk d -> to_jval fr d
  | _ -> None

(* THE LOSSLESS READER REBUILDS THE PARSE. For every document, every limits record, every byte
   verdict and every depth budget, reading it losslessly and converting the document answers
   exactly the value the strict limited parse accepts it with, and nothing where that parse
   refuses it. Production: `JDoc.read` then `JDoc.toJVal` is `Json.read` under `RejectNull` on
   every input `Json.read` accepts, and `Json.read` accepts exactly the inputs whose lossless
   document converts. *)
[@@ noextract_to "FSharp"]
let lossless_reader_rebuilds_the_parse
  (fr: list P.ch -> P.freadv) (cap: string) (b: list unit) (lim: P.lims) (bytes_ok: bool)
  (s: list P.ch)
  : Lemma (ensures converted fr (read_lossless fr cap b lim bytes_ok s) ==
                   (match P.parse_limited fr cap P.RejectNull b lim bytes_ok s with
                    | P.ROk v -> Some v
                    | _ -> None)) =
  lossless_is_parse_value fr cap lim b s

(* The corollary that says ONE GRAMMAR: a document the lossless reader refuses — by the grammar,
   a bound, or trailing input — the strict parse refuses too. *)
[@@ noextract_to "FSharp"]
let lossless_refusal_is_a_parse_refusal
  (fr: list P.ch -> P.freadv) (cap: string) (b: list unit) (lim: P.lims) (bytes_ok: bool)
  (s: list P.ch)
  : Lemma (ensures DOk? (read_lossless fr cap b lim bytes_ok s) \/
                   ~(P.ROk? (P.parse_limited fr cap P.RejectNull b lim bytes_ok s))) =
  lossless_reader_rebuilds_the_parse fr cap b lim bytes_ok s

(* ======================================================================================
   5. The decode layer over the document (F#: `JDoc.toJVal` with its refusals, and
      `JDocDecoder.ofDecoder`).

      A decoder over `JVal` (`DecodeLayer`'s `dec`, parametric in its two NUMERIC carriers over a
      value with no null) reads a document through the conversion: `lower` is `JDoc.toJVal` over the
      decode layer's value, a null refused `NotAdmitted` and a number its carrier cannot hold
      `OutOfRange`, each AT ITS PATH, the first in document order; `nd` is the host's reading of a
      number text, which answers a NUMBER (`numval`) or nothing.
   ====================================================================================== *)

let numval (num flt: eqtype) = v: W.jval num flt { W.JInt? v \/ W.JFloat? v }

let ddoc (t: eqtype) = jdoc string t

let rec lower (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (d: ddoc t)
  : Tot (D.result (W.jval num flt) D.derr) (decreases d) =
  match d with
  | DNull -> D.Error (D.make D.NotAdmitted)
  | DBool b -> D.Ok (W.JBool b)
  | DStr s -> D.Ok (W.JStr s)
  | DNum n ->
    (match nd n with
     | Some v -> D.Ok v
     | None -> D.Error (D.make D.OutOfRange))
  | DArr xs ->
    (match lower_items nd 0 xs with
     | D.Ok ys -> D.Ok (W.JArr ys)
     | D.Error e -> D.Error e)
  | DObj fs ->
    (match lower_fields nd [] fs with
     | D.Ok gs -> D.Ok (W.JObj gs)
     | D.Error e -> D.Error e)

and lower_items (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (i: int) (xs: list (ddoc t))
  : Tot (D.result (list (W.jval num flt)) D.derr) (decreases xs) =
  match xs with
  | [] -> D.Ok []
  | x :: rest ->
    (match lower nd x with
     | D.Error e -> D.Error (D.under (D.Index i) e)
     | D.Ok v ->
       (match lower_items nd (i + 1) rest with
        | D.Ok vs -> D.Ok (v :: vs)
        | D.Error e -> D.Error e))

(* `seen` is the names already lowered: a repeated name is refused `NotAdmitted` at the repeated
   member, before its value is lowered (F#: the conversion's member set), so every member path the
   conversion answers names the member it means. *)
and lower_fields (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (seen: list string)
  (fs: list (string & ddoc t))
  : Tot (D.result (list (string & W.jval num flt)) D.derr) (decreases fs) =
  match fs with
  | [] -> D.Ok []
  | (k, x) :: rest ->
    if mem k seen then D.Error (D.under (D.Key k) (D.make D.NotAdmitted))
    else
    (match lower nd x with
     | D.Error e -> D.Error (D.under (D.Key k) e)
     | D.Ok v ->
       (match lower_fields nd (k :: seen) rest with
        | D.Ok vs -> D.Ok ((k, v) :: vs)
        | D.Error e -> D.Error e))

(* F#: `JDocDecoder.ofDecoder` — the document converted, then read by a decoder of the layer. *)
let of_decoder (#t #num #flt: eqtype) (#a: Type0) (nd: t -> option (numval num flt)) (dc: D.dec num flt a)
  (x: ddoc t) : Tot (D.result a D.derr) =
  match lower nd x with
  | D.Error e -> D.Error e
  | D.Ok v -> dc v

(* ---- paths into a document: `DecodeLayer`'s, over the document ---- *)

(* A member is `DecodeLayer.memb` (the FIRST of a name) and a name's presence `DecodeLayer.has_key`,
   both generic in what a member holds. *)

let dstep (#t: eqtype) (s: D.seg) (d: ddoc t) : Tot (option (ddoc t)) =
  match s, d with
  | D.Key k, DObj fs -> D.memb k fs
  | D.Index i, DArr xs -> if i >= 0 then D.try_item i xs else None
  | _, _ -> None

let rec dresolve (#t: eqtype) (p: list D.seg) (d: ddoc t) : Tot (option (ddoc t)) (decreases p) =
  match p with
  | [] -> Some d
  | s :: rest ->
    (match dstep s d with
     | Some v -> dresolve rest v
     | None -> None)

(* `DecodeLayer.resolves_in`, over the document. *)
let d_resolves_in (#t: eqtype) (d: ddoc t) (e: D.derr) : Tot bool =
  match e.D.code with
  | D.InvalidJson -> (match e.D.path with [] -> true | _ -> false)
  | D.MissingField ->
    (match W.rev e.D.path with
     | D.Key k :: parent_rev ->
       (match dresolve (W.rev parent_rev) d with
        | Some (DObj fields) -> not (D.has_key k fields)
        | _ -> false)
     | _ -> false)
  | _ ->
    (match dresolve e.D.path d with
     | Some _ -> true
     | None -> false)

[@@ noextract_to "FSharp"]
let d_ok_at (#t: eqtype) (d: ddoc t) (e: D.derr) : bool =
  e.D.code <> D.InvalidJson && d_resolves_in d e

[@@ noextract_to "FSharp"]
let d_good (#t: eqtype) (#a: Type0) (d: ddoc t) (r: D.result a D.derr) : bool =
  match r with
  | D.Ok _ -> true
  | D.Error e -> d_ok_at d e

(* ---- the conversion's own refusals resolve in the document ---- *)

[@@ noextract_to "FSharp"]
let rec lower_refusal_resolves (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (d: ddoc t)
  : Lemma (ensures (match lower nd d with
                    | D.Error e -> (e.D.code = D.NotAdmitted \/ e.D.code = D.OutOfRange) /\
                                   Some? (dresolve e.D.path d)
                    | D.Ok _ -> True))
          (decreases d) =
  match d with
  | DArr xs -> lower_items_refusal_resolves nd 0 xs
  | DObj fs -> lower_fields_refusal_resolves nd [] fs
  | _ -> ()

and lower_items_refusal_resolves (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (i: int)
  (xs: list (ddoc t))
  : Lemma (requires i >= 0)
          (ensures (match lower_items nd i xs with
                    | D.Error e -> (e.D.code = D.NotAdmitted \/ e.D.code = D.OutOfRange) /\
                                   (match e.D.path with
                                    | D.Index j :: p -> j >= i /\
                                      (match D.try_item (j - i) xs with
                                       | Some y -> Some? (dresolve p y)
                                       | None -> False)
                                    | _ -> False)
                    | D.Ok _ -> True))
          (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
    lower_refusal_resolves nd x;
    lower_items_refusal_resolves nd (i + 1) rest

and lower_fields_refusal_resolves (#t #num #flt: eqtype) (nd: t -> option (numval num flt))
  (seen: list string) (fs: list (string & ddoc t))
  : Lemma (ensures (match lower_fields nd seen fs with
                    | D.Error e -> (e.D.code = D.NotAdmitted \/ e.D.code = D.OutOfRange) /\
                                   (match e.D.path with
                                    | D.Key k :: p ->
                                      (match D.memb k fs with
                                       | Some y -> Some? (dresolve p y)
                                       | None -> False) /\
                                      (mem k seen ==> p == [])
                                    | _ -> False)
                    | D.Ok _ -> True))
          (decreases fs) =
  match fs with
  | [] -> ()
  | (k, x) :: rest ->
    lower_refusal_resolves nd x;
    lower_fields_refusal_resolves nd (k :: seen) rest

(* ---- a path that resolves in the lowered value resolves in the document ---- *)

[@@ noextract_to "FSharp"]
let rec memb_lowers (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (k: string)
  (seen: list string) (fs: list (string & ddoc t)) (gs: list (string & W.jval num flt))
  : Lemma (requires lower_fields nd seen fs == D.Ok gs)
          (ensures (match D.memb k gs with
                    | Some w -> (match D.memb k fs with Some y -> lower nd y == D.Ok w | None -> False)
                    | None -> True) /\
                   D.has_key k fs == D.has_key k gs)
          (decreases fs) =
  match fs with
  | [] -> ()
  | (k', x) :: rest ->
    (match lower nd x, lower_fields nd (k' :: seen) rest with
     | D.Ok _, D.Ok vs -> memb_lowers nd k (k' :: seen) rest vs
     | _ -> ())

[@@ noextract_to "FSharp"]
let rec item_lowers (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (j: int) (i: int)
  (xs: list (ddoc t)) (ys: list (W.jval num flt))
  : Lemma (requires lower_items nd j xs == D.Ok ys)
          (ensures (match D.try_item i ys with
                    | Some w -> (match D.try_item i xs with Some y -> lower nd y == D.Ok w | None -> False)
                    | None -> True))
          (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
    (match lower nd x, lower_items nd (j + 1) rest with
     | D.Ok _, D.Ok vs -> if i = 0 then () else item_lowers nd (j + 1) (i - 1) rest vs
     | _ -> ())

(* A document that lowers to an object IS an object, with the same member names: a number lowers
   to a number (`numval`), and nothing else lowers to an object. *)
[@@ noextract_to "FSharp"]
let lowers_to_object (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (y: ddoc t)
  (gs: list (string & W.jval num flt))
  : Lemma (requires lower nd y == D.Ok (W.JObj gs))
          (ensures DObj? y /\ lower_fields nd [] (DObj?.fields y) == D.Ok gs) =
  match y with
  | DNum n -> (match nd n with Some v -> () | None -> ())
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec resolve_lowers (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (p: list D.seg)
  (x: ddoc t) (v: W.jval num flt)
  : Lemma (requires lower nd x == D.Ok v)
          (ensures (match D.resolve p v with
                    | Some w -> (match dresolve p x with Some y -> lower nd y == D.Ok w | None -> False)
                    | None -> True))
          (decreases p) =
  match p with
  | [] -> ()
  | D.Key k :: rest ->
    (match v with
     | W.JObj gs ->
       lowers_to_object nd x gs;
       memb_lowers nd k [] (DObj?.fields x) gs;
       (match D.memb k gs, D.memb k (DObj?.fields x) with
        | Some w, Some y -> resolve_lowers nd rest y w
        | _ -> ())
     | _ -> ())
  | D.Index i :: rest ->
    (match v, x with
     | W.JArr ys, DArr xs ->
       if i >= 0 then (
         item_lowers nd 0 i xs ys;
         match D.try_item i ys, D.try_item i xs with
         | Some w, Some y -> resolve_lowers nd rest y w
         | _ -> ())
       else ()
     | W.JArr ys, DNum n -> (match nd n with Some _ -> () | None -> ())
     | _ -> ())

(* WHAT FOLLOWS FROM THE INSTANTIATION. Every decoder the decode layer proves sound — every
   combinator `DecodeLayer.fst` proves sound over `WireDecode`'s value, and every decoder a domain
   composes from them — reads a document through `ofDecoder` and refuses only at a path that
   resolves IN THE DOCUMENT, or names a member a resolving object of it lacks: the decoder's own
   refusals carry over through the conversion, and the conversion's (a null, a number past the
   carrier) are at the value they name. Production: `JDocDecoder.ofDecoder d` answers a refusal a
   caller can walk to in the document it handed in, never in an intermediate value it never saw. *)
[@@ noextract_to "FSharp"]
let of_decoder_is_sound (#t #num #flt: eqtype) (#a: Type0) (nd: t -> option (numval num flt))
  (dc: D.dec num flt a) (x: ddoc t)
  : Lemma (requires D.sound dc) (ensures d_good x (of_decoder nd dc x)) =
  lower_refusal_resolves nd x;
  match lower nd x with
  | D.Error _ -> ()
  | D.Ok v ->
    D.sound_elim dc v;
    (match dc v with
     | D.Ok _ -> ()
     | D.Error e ->
       resolve_lowers nd e.D.path x v;
       (match W.rev e.D.path with
        | D.Key k :: parent_rev ->
          resolve_lowers nd (W.rev parent_rev) x v;
          (match D.resolve (W.rev parent_rev) v, dresolve (W.rev parent_rev) x with
           | Some (W.JObj gs), Some y ->
             lowers_to_object nd y gs;
             memb_lowers nd k [] (DObj?.fields y) gs
           | _ -> ())
        | _ -> ()))

(* ======================================================================================
   6. The document decoder (F#: `JDocDecoder`, Document.fs) — the decode algebra at the
      document's own carrier, and theorem 3 for it (Phase 658).

      `JDocDecoder` is `DecodeOver` instantiated at `JDoc`, as `Decoder` is at `JVal`; so is the
      model: every combinator below is `DecodeLayer`'s generic one at `dcarrier`, except the
      readers the document owns — `json`, `nullable` (the one reader of a null as a value),
      `bool`, `numberText` (a number AS WRITTEN), `int` and `float` (a number by the host's
      reading of its text, `nd`, the reading the conversion uses, refused `OutOfRange` AT THE
      NUMBER) — and the compositions `JDocDecoder` spells itself (`pair`, `fieldOr`, `list`,
      `closed`). The statement is section 5's `d_good`: a refusal names a path that resolves IN
      THE DOCUMENT (`dresolve`), or a member a resolving object of it lacks, and is never
      `InvalidJson`. The carrier law between the algebra's path reading and `dresolve` is
      faithfulness (`d_ok_at_is_over`), proved here.
   ====================================================================================== *)

(* F#: `JDocDecoder`'s private `carrier`, its three views. *)
let dmembers (#t: eqtype) (d: ddoc t) : Tot (option (list (string & ddoc t))) =
  match d with
  | DObj fields -> Some fields
  | _ -> None

let ditems (#t: eqtype) (d: ddoc t) : Tot (option (list (ddoc t))) =
  match d with
  | DArr xs -> Some xs
  | _ -> None

let dtext (#t: eqtype) (d: ddoc t) : Tot (option string) =
  match d with
  | DStr s -> Some s
  | _ -> None

let dcarrier (#t: eqtype) : D.carrier (ddoc t) = { D.members_of = dmembers; D.items_of = ditems; D.text_of = dtext }

(* F#: `JDocDecoder<'T> = JDoc -> Result<'T, DecodeError>`. *)
let jdec (t: eqtype) (a: Type0) = D.dec_over (ddoc t) a

(* F#: `JDocDecoder.succeed`. *)
let jd_succeed (#t: eqtype) (#a: Type0) (v: a) : jdec t a = fun _ -> D.Ok v

(* F#: `JDocDecoder.fail`. *)
let jd_fail (#t: eqtype) (#a: Type0) (c: D.root_code) : jdec t a = fun el -> D.fail_over c el

(* F#: `JDocDecoder.map`. *)
let jd_map (#t: eqtype) (#a #b: Type0) (g: a -> b) (d: jdec t a) : jdec t b = fun el -> D.map_over g d el

(* F#: `JDocDecoder.bind`. *)
let jd_bind (#t: eqtype) (#a #b: Type0) (g: a -> jdec t b) (d: jdec t a) : jdec t b = fun el -> D.bind_over g d el

(* F#: `JDocDecoder.pair` — `a |> bind (fun x -> b |> map (f x))`. *)
let jd_pair (#t: eqtype) (#a #b #c: Type0) (da: jdec t a) (db: jdec t b) (f: a -> b -> c) : jdec t c =
  jd_bind (fun (x: a) -> jd_map (f x) db) da

(* F#: `JDocDecoder.andThen`. *)
let jd_and_then (#t: eqtype) (#a #b: Type0) (g: a -> D.result b D.root_code) (d: jdec t a) : jdec t b =
  fun el -> D.and_then_over g d el

(* F#: `JDocDecoder.json`. *)
let jd_json (#t: eqtype) : jdec t (ddoc t) = fun el -> D.Ok el

(* F#: `JDocDecoder.nullable` — `None` for a null, else `d`'s answer under `Some`. *)
let jd_nullable (#t: eqtype) (#a: Type0) (d: jdec t a) : jdec t (option a) =
  fun el ->
    match el with
    | DNull -> D.Ok None
    | other -> (match d other with D.Ok x -> D.Ok (Some x) | D.Error e -> D.Error e)

(* F#: `JDocDecoder.str`. *)
let jd_str (#t: eqtype) : jdec t string = fun el -> D.text_over dcarrier el

(* F#: `JDocDecoder.bool`. *)
let jd_bool (#t: eqtype) : jdec t bool =
  fun el ->
    match el with
    | DBool b -> D.Ok b
    | other -> D.Error (D.wrong_kind_over dcarrier other)

(* F#: `JDocDecoder.numberText` — the token, exactly as written. *)
let jd_number_text (#t: eqtype) : jdec t t =
  fun el ->
    match el with
    | DNum n -> D.Ok n
    | other -> D.Error (D.wrong_kind_over dcarrier other)

(* F#: `JDocDecoder.int` — `JDoc.tryInt32`, the number's reading as `ReadsInt`; any other reading
   (a float token, an integer past the carrier, a text outside the grammar) is `OutOfRange` AT THE
   NUMBER. `nd` is the host's reading of a number text, the one `lower` uses. *)
let jd_int (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) : jdec t num =
  fun el ->
    match el with
    | DNum n ->
        (match nd n with
         | Some (W.JInt i) -> D.Ok i
         | _ -> D.Error (D.make D.OutOfRange))
    | other -> D.Error (D.wrong_kind_over dcarrier other)

(* F#: `JDocDecoder.float` — `JDoc.tryFloat`: an integer reading widened by `to_flt` (F#:
   `float i`), a float reading as it is, nothing else; a number with no reading is `OutOfRange`
   at the number. *)
let jd_float (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (to_flt: num -> flt) : jdec t flt =
  fun el ->
    match el with
    | DNum n ->
        (match nd n with
         | Some (W.JInt i) -> D.Ok (to_flt i)
         | Some (W.JFloat f) -> D.Ok f
         | _ -> D.Error (D.make D.OutOfRange))
    | other -> D.Error (D.wrong_kind_over dcarrier other)

(* F#: `JDocDecoder.items`, `obj`. *)
let jd_items (#t: eqtype) : jdec t (list (ddoc t)) = fun el -> D.items_over dcarrier el
let jd_obj (#t: eqtype) : jdec t (list (string & ddoc t)) = fun el -> D.members_over dcarrier el

(* F#: `JDocDecoder.field` — a member whose value is `null` is PRESENT, and `d` reads the null. *)
let jd_field (#t: eqtype) (#a: Type0) (name: string) (d: jdec t a) : jdec t a =
  fun el -> D.field_over dcarrier name d el

(* F#: `JDocDecoder.optField`. *)
let jd_opt_field (#t: eqtype) (#a: Type0) (name: string) (d: jdec t a) : jdec t (option a) =
  fun el -> D.opt_field_over dcarrier name d el

(* F#: `JDocDecoder.fieldOr`. *)
let jd_field_or (#t: eqtype) (#a: Type0) (name: string) (fallback: a) (d: jdec t a) : jdec t a =
  jd_map (fun (o: option a) -> match o with Some x -> x | None -> fallback) (jd_opt_field name d)

(* F#: `JDocDecoder.at`. *)
let jd_at (#t: eqtype) (#a: Type0) (path: list D.seg) (d: jdec t a) : jdec t a =
  fun el -> D.at_over dcarrier path d el

(* F#: `JDocDecoder.mapListIndexed`, `list`, `boundedList`, `listAll`. *)
let jd_map_list_indexed (#t: eqtype) (#a: Type0) (g: int -> jdec t a) : jdec t (list a) =
  fun el -> D.map_list_indexed_over dcarrier g el

let jd_list (#t: eqtype) (#a: Type0) (d: jdec t a) : jdec t (list a) = jd_map_list_indexed (fun _ -> d)

let jd_bounded_list (#t: eqtype) (#a: Type0) (max_items: int) (d: jdec t a) : jdec t (list a) =
  fun el -> D.bounded_list_over dcarrier max_items d el

let jd_list_all (#t: eqtype) (#a: Type0) (d: jdec t a) (el: ddoc t) : Tot (D.result (list a) (list D.derr)) =
  D.list_all_over dcarrier d el

(* F#: `JDocDecoder.sequence`, `all`. *)
let jd_sequence (#t: eqtype) (#a: Type0) (ds: list (jdec t a)) : jdec t (list a) = fun el -> D.sequence_over ds el

let jd_all (#t: eqtype) (#a: Type0) (ds: list (jdec t a)) (el: ddoc t) : Tot (D.result (list a) (list D.derr)) =
  D.all_over ds el

(* F#: `JDocDecoder.oneOf`, `tagDispatch`. *)
let jd_one_of (#t: eqtype) (#a: Type0) (cases: list (string & a)) : jdec t a = fun el -> D.one_of_over dcarrier cases el

let jd_tag_dispatch (#t: eqtype) (#a: Type0) (key: string) (cases: list (string & jdec t a)) : jdec t a =
  fun el -> D.tag_dispatch_over dcarrier key cases el

(* F#: `JDocDecoder.undeclared`, `members`, `closed`. *)
let jd_undeclared (#t: eqtype) (known: list string) (el: ddoc t) : Tot (list D.derr) =
  D.undeclared_over dcarrier known el

let jd_members (#t: eqtype) (known: list string) : jdec t unit = fun el -> D.members_only_over dcarrier known el

let jd_closed (#t: eqtype) (#a: Type0) (known: list string) (d: jdec t a) : jdec t a =
  jd_bind (fun () -> d) (jd_members known)

(* ---- what a document decoder owes the document: `d_good` (section 5), quantified ---- *)

[@@ noextract_to "FSharp"]
let d_sound (#t: eqtype) (#a: Type0) (dd: jdec t a) : prop = forall (x: ddoc t). d_good x (dd x)

[@@ noextract_to "FSharp"]
let rec d_all_ok (#t: eqtype) (x: ddoc t) (es: list D.derr) : Tot bool =
  match es with
  | [] -> true
  | e :: rest -> d_ok_at x e && d_all_ok x rest

[@@ noextract_to "FSharp"]
let d_good_all (#t: eqtype) (#a: Type0) (x: ddoc t) (r: D.result a (list D.derr)) : bool =
  match r with
  | D.Ok _ -> true
  | D.Error es -> (match es with [] -> false | _ -> d_all_ok x es)

[@@ noextract_to "FSharp"]
let d_sound_all (#t: eqtype) (#a: Type0) (rd: ddoc t -> D.result a (list D.derr)) : prop =
  forall (x: ddoc t). d_good_all x (rd x)

(* ---- FAITHFULNESS of the document's carrier: its view is the document's own object, array and
   string, so the algebra's path law at `dcarrier` is `dresolve`, and `d_ok_at` is `ok_at_over`. ---- *)

[@@ noextract_to "FSharp"]
let rec dresolve_is_over (#t: eqtype) (p: list D.seg) (x: ddoc t)
  : Lemma (ensures dresolve p x == D.resolve_over dcarrier p x) (decreases p) =
  match p with
  | [] -> ()
  | s :: rest -> (match dstep s x with Some y -> dresolve_is_over rest y | None -> ())

let d_ok_at_is_over (#t: eqtype) (x: ddoc t) (e: D.derr) : Lemma (d_ok_at x e == D.ok_at_over dcarrier x e) =
  dresolve_is_over e.D.path x;
  match W.rev e.D.path with
  | D.Key k :: parent_rev -> dresolve_is_over (W.rev parent_rev) x
  | _ -> ()

[@@ noextract_to "FSharp"]
let rec d_all_ok_is_over (#t: eqtype) (x: ddoc t) (es: list D.derr)
  : Lemma (ensures d_all_ok x es == D.all_ok_over dcarrier x es) =
  match es with
  | [] -> ()
  | e :: rest -> d_ok_at_is_over x e; d_all_ok_is_over x rest

[@@ noextract_to "FSharp"]
let d_sound_is_over (#t: eqtype) (#a: Type0) (dd: jdec t a) : Lemma (d_sound dd <==> D.sound_over dcarrier dd) =
  let pf (x: ddoc t) : Lemma (d_good x (dd x) == D.good_over dcarrier x (dd x)) =
    match dd x with
    | D.Ok _ -> ()
    | D.Error e -> d_ok_at_is_over x e in
  FStar.Classical.forall_intro pf

[@@ noextract_to "FSharp"]
let d_sound_all_is_over (#t: eqtype) (#a: Type0) (rd: ddoc t -> D.result a (list D.derr))
  : Lemma (d_sound_all rd <==> D.sound_all_over dcarrier rd) =
  let pf (x: ddoc t) : Lemma (d_good_all x (rd x) == D.good_all_over dcarrier x (rd x)) =
    match rd x with
    | D.Ok _ -> ()
    | D.Error es -> d_all_ok_is_over x es in
  FStar.Classical.forall_intro pf

[@@ noextract_to "FSharp"]
let d_sound_to_over (#t: eqtype) (#a: Type0) (dd: jdec t a)
  : Lemma (requires d_sound dd) (ensures D.sound_over dcarrier dd) = d_sound_is_over dd

[@@ noextract_to "FSharp"]
let d_sound_of_over (#t: eqtype) (#a: Type0) (dd: jdec t a)
  : Lemma (requires D.sound_over dcarrier dd) (ensures d_sound dd) = d_sound_is_over dd

[@@ noextract_to "FSharp"]
let d_sound_elim (#t: eqtype) (#a: Type0) (dd: jdec t a) (x: ddoc t)
  : Lemma (requires d_sound dd) (ensures d_good x (dd x)) = ()

(* ---- theorem 3 for `JDocDecoder`, block by block ---- *)

let jd_succeed_sound (#t: eqtype) (#a: Type0) (v: a) : Lemma (d_sound (jd_succeed #t v)) = ()

let jd_fail_sound (#t: eqtype) (#a: Type0) (c: D.root_code) : Lemma (d_sound (jd_fail #t #a c)) =
  D.fail_sound_over #(ddoc t) #a dcarrier c; d_sound_of_over (jd_fail #t #a c)

let jd_map_sound (#t: eqtype) (#a #b: Type0) (g: a -> b) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_map g d)) =
  d_sound_to_over d; D.map_sound_over dcarrier g d; d_sound_of_over (jd_map g d)

let jd_bind_sound (#t: eqtype) (#a #b: Type0) (g: a -> jdec t b) (d: jdec t a)
  : Lemma (requires d_sound d /\ (forall (v: a). d_sound (g v))) (ensures d_sound (jd_bind g d)) =
  d_sound_to_over d;
  let each (v: a) : Lemma (D.sound_over dcarrier (g v)) = d_sound_to_over (g v) in
  FStar.Classical.forall_intro each;
  D.bind_sound_over dcarrier g d;
  d_sound_of_over (jd_bind g d)

let jd_pair_sound (#t: eqtype) (#a #b #c: Type0) (da: jdec t a) (db: jdec t b) (f: a -> b -> c)
  : Lemma (requires d_sound da /\ d_sound db) (ensures d_sound (jd_pair da db f)) =
  let each (x: a) : Lemma (d_sound (jd_map (f x) db)) = jd_map_sound (f x) db in
  FStar.Classical.forall_intro each;
  jd_bind_sound (fun (x: a) -> jd_map (f x) db) da

let jd_and_then_sound (#t: eqtype) (#a #b: Type0) (g: a -> D.result b D.root_code) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_and_then g d)) =
  d_sound_to_over d; D.and_then_sound_over dcarrier g d; d_sound_of_over (jd_and_then g d)

let jd_json_sound (#t: eqtype) : Lemma (d_sound (jd_json #t)) = ()

(* A null is a value: `nullable` reads it as `None`, and every other refusal is `d`'s. *)
let jd_nullable_sound (#t: eqtype) (#a: Type0) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_nullable d)) =
  let pf (x: ddoc t) : Lemma (d_good x (jd_nullable d x)) = d_sound_elim d x in
  FStar.Classical.forall_intro pf

let jd_str_sound (#t: eqtype) : Lemma (d_sound (jd_str #t)) =
  D.text_sound_over (dcarrier #t); d_sound_of_over (jd_str #t)

let jd_bool_sound (#t: eqtype) : Lemma (d_sound (jd_bool #t)) = ()

let jd_number_text_sound (#t: eqtype) : Lemma (d_sound (jd_number_text #t)) = ()

(* The number readers are sound for EVERY reading of a number text: a number with no reading is
   refused at the number itself, which the document holds. *)
let jd_int_sound (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) : Lemma (d_sound (jd_int nd)) = ()

let jd_float_sound (#t #num #flt: eqtype) (nd: t -> option (numval num flt)) (to_flt: num -> flt)
  : Lemma (d_sound (jd_float nd to_flt)) = ()

let jd_items_sound (#t: eqtype) : Lemma (d_sound (jd_items #t)) =
  D.items_sound_over (dcarrier #t); d_sound_of_over (jd_items #t)

let jd_obj_sound (#t: eqtype) : Lemma (d_sound (jd_obj #t)) =
  D.members_sound_over (dcarrier #t); d_sound_of_over (jd_obj #t)

let jd_field_sound (#t: eqtype) (#a: Type0) (name: string) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_field name d)) =
  d_sound_to_over d; D.field_sound_over dcarrier name d; d_sound_of_over (jd_field name d)

let jd_opt_field_sound (#t: eqtype) (#a: Type0) (name: string) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_opt_field name d)) =
  d_sound_to_over d; D.opt_field_sound_over dcarrier name d; d_sound_of_over (jd_opt_field name d)

let jd_field_or_sound (#t: eqtype) (#a: Type0) (name: string) (fallback: a) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_field_or name fallback d)) =
  jd_opt_field_sound name d;
  jd_map_sound (fun (o: option a) -> match o with Some x -> x | None -> fallback) (jd_opt_field name d)

let jd_at_sound (#t: eqtype) (#a: Type0) (p: list D.seg) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_at p d)) =
  d_sound_to_over d; D.at_sound_over dcarrier p d; d_sound_of_over (jd_at p d)

let jd_map_list_indexed_sound (#t: eqtype) (#a: Type0) (g: int -> jdec t a)
  : Lemma (requires forall (i: int). d_sound (g i)) (ensures d_sound (jd_map_list_indexed g)) =
  let each (i: int) : Lemma (D.sound_over dcarrier (g i)) = d_sound_to_over (g i) in
  FStar.Classical.forall_intro each;
  D.map_list_indexed_sound_over dcarrier g;
  d_sound_of_over (jd_map_list_indexed g)

let jd_list_sound (#t: eqtype) (#a: Type0) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_list d)) =
  jd_map_list_indexed_sound #t #a (fun _ -> d)

let jd_bounded_list_sound (#t: eqtype) (#a: Type0) (max_items: int) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_bounded_list max_items d)) =
  d_sound_to_over d; D.bounded_list_sound_over dcarrier max_items d; d_sound_of_over (jd_bounded_list max_items d)

let jd_list_all_sound (#t: eqtype) (#a: Type0) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound_all (jd_list_all d)) =
  d_sound_to_over d; D.list_all_sound_over dcarrier d; d_sound_all_is_over (jd_list_all d)

[@@ noextract_to "FSharp"]
let rec d_all_sound (#t: eqtype) (#a: Type0) (ds: list (jdec t a)) : prop =
  match ds with
  | [] -> True
  | d :: rest -> d_sound d /\ d_all_sound rest

[@@ noextract_to "FSharp"]
let rec d_all_sound_to_over (#t: eqtype) (#a: Type0) (ds: list (jdec t a))
  : Lemma (requires d_all_sound ds) (ensures D.all_sound_over dcarrier ds) =
  match ds with
  | [] -> ()
  | d :: rest -> d_sound_to_over d; d_all_sound_to_over rest

let jd_sequence_sound (#t: eqtype) (#a: Type0) (ds: list (jdec t a))
  : Lemma (requires d_all_sound ds) (ensures d_sound (jd_sequence ds)) =
  d_all_sound_to_over ds; D.sequence_sound_over dcarrier ds; d_sound_of_over (jd_sequence ds)

let jd_all_sound (#t: eqtype) (#a: Type0) (ds: list (jdec t a))
  : Lemma (requires d_all_sound ds) (ensures d_sound_all (jd_all ds)) =
  d_all_sound_to_over ds; D.all_d_sound_over dcarrier ds; d_sound_all_is_over (jd_all ds)

let jd_one_of_sound (#t: eqtype) (#a: Type0) (cases: list (string & a)) : Lemma (d_sound (jd_one_of #t cases)) =
  D.one_of_sound_over (dcarrier #t) cases; d_sound_of_over (jd_one_of #t cases)

[@@ noextract_to "FSharp"]
let rec d_table_sound (#t: eqtype) (#a: Type0) (cases: list (string & jdec t a)) : prop =
  match cases with
  | [] -> True
  | (_, d) :: rest -> d_sound d /\ d_table_sound rest

[@@ noextract_to "FSharp"]
let rec d_table_sound_to_over (#t: eqtype) (#a: Type0) (cases: list (string & jdec t a))
  : Lemma (requires d_table_sound cases) (ensures D.table_sound_over dcarrier cases) =
  match cases with
  | [] -> ()
  | (_, d) :: rest -> d_sound_to_over d; d_table_sound_to_over rest

let jd_tag_dispatch_sound (#t: eqtype) (#a: Type0) (key: string) (cases: list (string & jdec t a))
  : Lemma (requires d_table_sound cases) (ensures d_sound (jd_tag_dispatch key cases)) =
  d_table_sound_to_over cases; D.tag_dispatch_sound_over dcarrier key cases; d_sound_of_over (jd_tag_dispatch key cases)

let jd_undeclared_sound (#t: eqtype) (known: list string) (x: ddoc t) : Lemma (d_all_ok x (jd_undeclared known x)) =
  D.undeclared_sound_over dcarrier known x; d_all_ok_is_over x (jd_undeclared known x)

let jd_members_sound (#t: eqtype) (known: list string) : Lemma (d_sound (jd_members #t known)) =
  D.members_only_sound_over (dcarrier #t) known; d_sound_of_over (jd_members #t known)

let jd_closed_sound (#t: eqtype) (#a: Type0) (known: list string) (d: jdec t a)
  : Lemma (requires d_sound d) (ensures d_sound (jd_closed known d)) =
  jd_members_sound #t known;
  jd_bind_sound (fun () -> d) (jd_members known)

(* THEOREM 3 FOR THE DOCUMENT DECODER, the statement a caller reads: a document decoder built from
   `JDocDecoder`'s blocks, each sound by its lemma above, refuses a document only at a path that
   resolves IN THE DOCUMENT, or names a member a resolving object of it lacks, and never with
   `InvalidJson` — the same law, by the same step rule, as `DecodeLayer.refusal_paths_resolve`
   states for `JVal`. *)
let document_refusal_paths_resolve (#t: eqtype) (#a: Type0) (d: jdec t a) (x: ddoc t)
  : Lemma (requires d_sound d)
          (ensures (match d x with D.Error e -> d_resolves_in x e /\ e.D.code <> D.InvalidJson | D.Ok _ -> True)) =
  d_sound_elim d x

(* ======================================================================================
   TWINS (Phase 309) — the extractor premise, sampled at this model. What a twin is, why it
   is here and how the list is evaluated are `Twin`'s (Phase 504), which every model opens.
   ====================================================================================== *)

(* `{"a":null,"b":-12.50}` — a document the lossless reading reads and the strict parse refuses. *)
let null_doc: list P.ch =
  [ P.CLBrace; P.CQuote; P.CLa; P.CQuote; P.CColon; P.CLn; P.CLu; P.CLl; P.CLl; P.CComma;
    P.CQuote; P.CLb; P.CQuote; P.CColon; P.CMinus; P.CD1; P.CD2; P.CDot; P.CD5; P.CD0; P.CRBrace ]

let null_doc_read: dresult = read_lossless (fun _ -> P.FFinite) "c" [ (); () ] P.unbounded true null_doc

let null_doc_reads_as_written: bool =
  null_doc_read = DOk (DObj [ ([ P.OLit P.CLa ], DNull);
                              ([ P.OLit P.CLb ], DNum [ P.CMinus; P.CD1; P.CD2; P.CDot; P.CD5; P.CD0 ]) ])

let null_doc_converts_to_nothing: bool = None? (converted (fun _ -> P.FFinite) null_doc_read)

(* `[-12.50]` — a document both read, the lossless reading to the token and the parse to `JFloat`. *)
let num_doc: list P.ch = [ P.CLBrack; P.CMinus; P.CD1; P.CD2; P.CDot; P.CD5; P.CD0; P.CRBrack ]

let num_doc_agrees: bool =
  converted (fun _ -> P.FFinite) (read_lossless (fun _ -> P.FFinite) "c" [ () ] P.unbounded true num_doc)
  = Some (P.JArr [ P.JFloat [ P.CMinus; P.CD1; P.CD2; P.CDot; P.CD5; P.CD0 ] ])

(* `[true, null]` lowered: the null is refused `NotAdmitted` at its index. A top-level boolean, because a
   discriminator inside the twins list extracts to a multi-line match F#'s parser refuses there. *)
let lower_refuses_the_null: bool =
  match lower #string #string #string (fun _ -> None) (DArr [ DBool true; DNull ]) with
  | D.Error e -> e.D.code = D.NotAdmitted && e.D.path = [ D.Index 1 ]
  | D.Ok _ -> false
(* `{"id":null,"qty":412000.00,"tags":[null,"x"]}`, read through the document decoder's own blocks. *)
let foreign_doc : ddoc string = DObj [ ("id", DNull); ("qty", DNum "412000.00"); ("tags", DArr [ DNull; DStr "x" ]) ]

let jd_null_member_is_wrong_kind_at_its_path: bool =
  match jd_field "id" jd_str foreign_doc with
  | D.Error e -> e.D.code = D.WrongKind && e.D.path = [ D.Key "id" ] && d_resolves_in foreign_doc e
  | D.Ok _ -> false

let jd_nullable_reads_the_nulls: bool =
  jd_field "tags" (jd_list (jd_nullable jd_str)) foreign_doc = D.Ok [ None; Some "x" ]

let jd_number_text_keeps_the_scale: bool = jd_field "qty" jd_number_text foreign_doc = D.Ok "412000.00"

let jd_int_refuses_at_the_number: bool =
  match jd_field "qty" (jd_int #string #int #bool (fun _ -> None)) foreign_doc with
  | D.Error e -> e.D.code = D.OutOfRange && e.D.path = [ D.Key "qty" ] && d_resolves_in foreign_doc e
  | D.Ok _ -> false

let twins : list twin = [
  { tname = "the-lossless-reading-keeps-a-null-and-a-number-as-written";
    tholds = (fun () -> null_doc_reads_as_written) };
  { tname = "a-document-with-a-null-does-not-convert";
    tholds = (fun () -> null_doc_converts_to_nothing) };
  { tname = "a-converting-document-is-the-parse";
    tholds = (fun () -> num_doc_agrees) };
  { tname = "the-float-flag-reads-the-exponent";
    tholds = (fun () -> is_float_tok [ P.CD1; P.CUe; P.CD2 ] = true) };
  { tname = "the-float-flag-of-an-integer-is-false";
    tholds = (fun () -> is_float_tok [ P.CMinus; P.CD1; P.CD2 ] = false) };
  { tname = "lower-refuses-a-null-not-admitted";
    tholds = (fun () -> lower_refuses_the_null) };
  { tname = "the-document-decoder-refuses-a-null-member-as-wrong-kind-at-it";
    tholds = (fun () -> jd_null_member_is_wrong_kind_at_its_path) };
  { tname = "nullable-reads-each-null-as-none";
    tholds = (fun () -> jd_nullable_reads_the_nulls) };
  { tname = "number-text-keeps-the-scale";
    tholds = (fun () -> jd_number_text_keeps_the_scale) };
  { tname = "int-refuses-a-number-it-cannot-read-at-the-number";
    tholds = (fun () -> jd_int_refuses_at_the_number) } ]

let _ = assert_norm (twins_hold twins == true)
