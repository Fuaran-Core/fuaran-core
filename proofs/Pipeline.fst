(*
   Pipeline — the reference transform evaluator is total, and its row-evaluation count is
   additive, monotone in the pipeline prefix and bounded by the §21.8 expression-node limit
   (fuaran-core Phase 154).

   WHAT IS MODELLED. `src/Fuaran.Core.DataFrame/DataFrame.fs`, the reference evaluator's
   counted driver and the part of it that evaluates EXPRESSIONS:

     - the closed DUs `ColExpr` (all thirteen cases, in declaration order) and `Transform` (all
       fourteen verbs, in declaration order);
     - the private expression evaluator `evalExpr`, clause for clause, with its four inner loops
       (`Coalesce`'s `go`, `Case`'s `go`, `InList`'s `go`, `ApplyFn`'s `evalArgs`);
     - the two verbs that evaluate an expression per row, `evalFilter` and `evalDerive` (with
       `inferType`, `colIndex` and the replace-or-append of a derived column);
     - `evalStep`'s dispatch, and the counted driver `evalPipelineWithInEnvCounted` with its cost
       model `costOf` — a `Filter` or a `Derive` is charged the row count of the frame it stands
       on, every other verb nothing — and the uncounted entry `evalPipelineWithInEnv`, which
       production defines as the counted one projected.

   THREE things are PARAMETERS rather than clauses, exactly as Phase 176 made the pipeline
   evaluator and Phase 177 the witness parameters.

     - The CELL ALGEBRA (`prims` below): the five leaf types — `Cell`, `BinOp`, `ScalarFn`,
       `ColumnType`, `NowGrain` — are type parameters, and the primitives `evalExpr` calls on
       them (`arith` / `comparison` / `logical` / `stringPred` by operator class, `castCell`,
       `applyScalar`, `compareCells` read at zero, `Cell.typeOf`, the `Bool` and `Null`
       constructors and readers) are fields of a record the host supplies, with the `EvalError`
       constructors `evalExpr` raises and the param environment `Map.tryFind` reads. What the
       model OWNS is the recursion over the closed expression DU and the short-circuit order of
       every clause — which is where totality and the node bound live.
     - The OTHER TWELVE VERBS (`step_eval`): `Project`, `GroupBy`, `Join`, `Window`, `Pivot`,
       `Unpivot`, `Sort`, `Distinct`, `Limit`, `Union`, `Intersect`, `Except` evaluate no
       expression and are charged nothing; their semantics is a function the host supplies,
       under ONE contract: on a well-formed frame it returns a well-formed frame or a named
       error. Their payloads are a single opaque type `p`, because no theorem here reads one.
     - The FRAME is the evaluator's working form (`Frame = { Cols; Rows }`), not the columnar
       `Table`: `toFrame` / `ofFrame` are the transpose each way (Phase 206) and are the bridge's,
       exactly as `ColumnOps.fst` starts from a table rather than a codec.

   WHAT IS PROVED, over any cell algebra, any environment, any step evaluator meeting its
   contract, and any pipeline:

     - `eval_total` — on a well-formed frame the counted evaluator returns `Ok` with a
       well-formed frame, or an `Error` that is EXACTLY the error of one named step: the pipeline
       splits as `pre @ (s :: post)`, `pre` evaluates to the frame `s` faced, and `s` failed there
       with that error. Termination is structural recursion over the closed DUs, checked by the
       prover; the one partial operation on the modelled path — `List.item i row` in the `Col`
       clause — is proved in range on a well-formed frame, and so are `evalDerive`'s `List.map2`
       lengths. That is the whole of "total" production can mean without a `try`.
     - `uncounted_is_projection` — `evalPipelineWithInEnv = counted |> Result.map fst`, the one-line
       identity it is. It REPLACES the shard's original `counted_agrees`: production has no second
       path to agree with (fuaran-core#154's refine finding 2).
     - `count_additive` — the count of `p1 @ p2` is the count of `p1` plus the count of `p2` from
       the frame `p1` left, and the pipeline fails exactly when one of the two halves does.
     - `budget_monotone` — a pipeline that evaluates has every prefix evaluate, at a count no larger.
     - `budget_bounded` — under the HYPOTHESIS that every expression the pipeline embeds is within
       `Limits.max_expr_nodes` (WIRE_FORMAT §21.8), the node-weighted cost — rows times expression
       nodes, per evaluating step — is at most `max_expr_nodes` times the row-evaluation count, and
       the two runs produce the same frame. The limit is a hypothesis on the pipeline, NEVER a
       refusal the evaluator performs: nothing under `src/` enforces §21.8 (the finding recorded in
       `README.md`), and the model adds no check that production does not make.
     - `visits_le_nodes` / `rows_visits_le` — the instrumented reading that makes the node weight an
       honest bound: `expr_visits` counts the `evalExpr` invocations one row costs, following every
       short circuit, and it is at most the expression's node count; over a step's rows it is at most
       rows times nodes, which is the weight `budget_bounded` charges.

   WHAT IS NOT CLAIMED. Anything about the twelve verbs the step evaluator stands for beyond its
   contract; anything about the cell primitives beyond their types (division by zero, overflow and
   coercion are the laws' — `Conformance.transformLaws`); that the host's `prims` ARE production's
   primitives — the differential instantiates them FROM production (each primitive read through the
   public `evalExprInRow` on a one-node expression) and compares the whole result.

   HOW TO READ IT. Every definition names its F# counterpart. The module opens nothing but `Limits`,
   for the one constant, restates `outcome` and the list helpers it needs, and extracts beside the
   other models into the same oracle assembly.

   Apache-2.0, like everything beside it.
*)
module Pipeline


(* ======================================================================================
   0. The list helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type0) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* F#: `List.length`. *)
let rec len (#a:Type0) (l:list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: `@`. *)
let rec app (#a:Type0) (l m:list a) : Tot (list a) =
  match l with
  | [] -> m
  | h :: t -> h :: app t m

(* F#: `List.item i l`, at an index the caller has proved in range — production's `List.item` throws
   past the end, so the refinement is exactly the obligation the model has to discharge. *)
let rec nth (#a:Type0) (l:list a) (i:nat{i < len l}) : Tot a (decreases l) =
  match l with
  | x :: t -> if i = 0 then x else nth t (i - 1)

(* F#: `row |> List.mapi (fun j cell -> if j = i then c else cell)` — `evalDerive`'s replace. *)
let rec set_at (#a:Type0) (i:nat) (v:a) (l:list a) : Tot (list a) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> if i = 0 then v :: t else x :: set_at (i - 1) v t

(* F#: `cols |> List.map fst` — the private `available`. *)
let rec names (#ty:Type0) (cols:list (string & ty)) : Tot (list string) =
  match cols with
  | [] -> []
  | (n, _) :: t -> n :: names t

(* F#: `cols |> List.tryFindIndex (fun (n, _) -> n = name)` — the private `colIndex`. The
   refinement carries the fact every caller needs: a found index is inside the schema. *)
let rec index_of (#ty:Type0) (name:string) (cols:list (string & ty))
  : Tot (o:option nat{match o with Some i -> i < len cols | None -> True}) =
  match cols with
  | [] -> None
  | (n, _) :: t ->
      if n = name then Some 0
      else (match index_of name t with Some i -> Some (i + 1) | None -> None)

let rec len_app (#a:Type0) (l m:list a) : Lemma (len (app l m) == len l + len m) =
  match l with
  | [] -> ()
  | _ :: t -> len_app t m

let rec len_set_at (#a:Type0) (i:nat) (v:a) (l:list a)
  : Lemma (ensures len (set_at i v l) == len l) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> if i = 0 then () else len_set_at (i - 1) v t

(* ======================================================================================
   1. The closed DUs (`ColExpr`, `Transform`) and the expression-node count.
   ====================================================================================== *)

(* F#: `ColExpr`, the thirteen cases in declaration order. `c` is `Cell`, `op` `BinOp`, `fn`
   `ScalarFn`, `ty` `ColumnType`, `g` `NowGrain`. *)
type colexpr (c op fn ty g:Type0) =
  | Col      : string -> colexpr c op fn ty g
  | Lit      : c -> colexpr c op fn ty g
  | Param    : string -> colexpr c op fn ty g
  | Binary   : op -> colexpr c op fn ty g -> colexpr c op fn ty g -> colexpr c op fn ty g
  | Not      : colexpr c op fn ty g -> colexpr c op fn ty g
  | Coalesce : list (colexpr c op fn ty g) -> colexpr c op fn ty g
  | Case     : list (colexpr c op fn ty g & colexpr c op fn ty g) -> colexpr c op fn ty g
               -> colexpr c op fn ty g
  | Cast     : ty -> colexpr c op fn ty g -> colexpr c op fn ty g
  | ApplyFn  : fn -> list (colexpr c op fn ty g) -> colexpr c op fn ty g
  | InList   : colexpr c op fn ty g -> list (colexpr c op fn ty g) -> colexpr c op fn ty g
  | IsNull   : colexpr c op fn ty g -> colexpr c op fn ty g
  | InParam  : colexpr c op fn ty g -> string -> colexpr c op fn ty g
  | Now      : g -> colexpr c op fn ty g

(* F#: `Transform`, the fourteen verbs in declaration order. `Filter` and `Derive` carry the only
   expressions a pipeline can embed (WIRE_FORMAT §21.8: "`filter` and `derive` are the only pipeline
   steps carrying an expression"); every other payload is the opaque `p`. *)
type transform (c op fn ty g p:Type0) =
  | Filter    : colexpr c op fn ty g -> transform c op fn ty g p
  | Project   : p -> transform c op fn ty g p
  | Derive    : string -> colexpr c op fn ty g -> transform c op fn ty g p
  | GroupBy   : p -> transform c op fn ty g p
  | Join      : p -> transform c op fn ty g p
  | Window    : p -> transform c op fn ty g p
  | Pivot     : p -> transform c op fn ty g p
  | Unpivot   : p -> transform c op fn ty g p
  | Sort      : p -> transform c op fn ty g p
  | Distinct  : transform c op fn ty g p
  | Limit     : p -> transform c op fn ty g p
  | Union     : p -> transform c op fn ty g p
  | Intersect : p -> transform c op fn ty g p
  | Except    : p -> transform c op fn ty g p

(* The §21.8 count: every `ColExpr` case is one node. *)
let rec expr_nodes (#c #op #fn #ty #g:Type0) (x:colexpr c op fn ty g) : Tot nat (decreases x) =
  match x with
  | Col _ -> 1
  | Lit _ -> 1
  | Param _ -> 1
  | Now _ -> 1
  | Binary _ a b -> 1 + expr_nodes a + expr_nodes b
  | Not a -> 1 + expr_nodes a
  | Cast _ a -> 1 + expr_nodes a
  | IsNull a -> 1 + expr_nodes a
  | InParam a _ -> 1 + expr_nodes a
  | Coalesce xs -> 1 + list_nodes xs
  | ApplyFn _ xs -> 1 + list_nodes xs
  | Case cs els -> 1 + case_nodes cs + expr_nodes els
  | InList s xs -> 1 + expr_nodes s + list_nodes xs
and list_nodes (#c #op #fn #ty #g:Type0) (xs:list (colexpr c op fn ty g)) : Tot nat (decreases xs) =
  match xs with
  | [] -> 0
  | x :: t -> expr_nodes x + list_nodes t
and case_nodes (#c #op #fn #ty #g:Type0) (cs:list (colexpr c op fn ty g & colexpr c op fn ty g))
  : Tot nat (decreases cs) =
  match cs with
  | [] -> 0
  | (w, t) :: r -> expr_nodes w + expr_nodes t + case_nodes r

(* ======================================================================================
   2. The cell algebra the host supplies, and `evalExpr`.
   ====================================================================================== *)

(* The primitives `evalExpr` calls, and the errors it raises. Each field names its F# source. *)
noeq type prims (c op fn ty g e:Type0) = {
  env_find        : string -> option c;             (* `Map.tryFind name env` *)
  env_names       : list string;                    (* `env |> Map.toList |> List.map fst` *)
  binary          : op -> c -> c -> outcome c e;    (* `arith` / `comparison` / `logical` / `stringPred`, by operator class *)
  as_bool         : c -> option bool;               (* the `Bool b` pattern *)
  is_null         : c -> bool;                      (* the `Null` pattern *)
  mk_bool         : bool -> c;                      (* `Bool` *)
  null_cell       : c;                              (* `Null` *)
  cast_cell       : ty -> c -> outcome c e;         (* `castCell` *)
  apply_fn        : fn -> list c -> outcome c e;    (* `applyScalar` *)
  cells_equal     : c -> c -> option bool;          (* `compareCells sv iv`: `Some 0` / `Some _` / `None` *)
  type_of         : c -> option ty;                 (* `Cell.typeOf` *)
  string_type     : ty;                             (* `StringType`, `inferType`'s default *)
  unknown_column  : string -> list string -> e;     (* `UnknownColumn` *)
  unbound_param   : string -> list string -> e;     (* `UnboundParam` *)
  unpinned_clock  : g -> e;                         (* `UnpinnedClock` *)
  not_non_bool    : e;                              (* `TypeError "not of a non-bool"` *)
  in_incompatible : e;                              (* `TypeError "in: comparison between incompatible types"` *)
}

(* A row that fits its schema: the one fact `List.item` needs. *)
type row_of (c:Type0) (n:nat) = r:list c{len r = n}

(* F#: `evalExpr env cols row e`, clause for clause. `Case`'s loop returns `None` where production's
   `go []` falls through to the `else` expression, so the fall-through is taken HERE, in the clause,
   and the loop recurses only over the list — the same evaluation, in the same order. *)
let rec eval_expr (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (x:colexpr c op fn ty g) : Tot (outcome c e) (decreases x) =
  match x with
  | Col name ->
      (match index_of name cols with
       | Some i -> Ok (nth row i)
       | None -> Error (p.unknown_column name (names cols)))
  | Lit v -> Ok v
  | Param name ->
      (match p.env_find name with
       | Some v -> Ok v
       | None -> Error (p.unbound_param name p.env_names))
  | Binary o a b ->
      (match eval_expr p cols row a with
       | Error err -> Error err
       | Ok av ->
           (match eval_expr p cols row b with
            | Error err -> Error err
            | Ok bv -> p.binary o av bv))
  | Not inner ->
      (match eval_expr p cols row inner with
       | Error err -> Error err
       | Ok v ->
           (match p.as_bool v with
            | Some b -> Ok (p.mk_bool (not b))
            | None -> if p.is_null v then Ok p.null_cell else Error p.not_non_bool))
  | Coalesce xs -> eval_coalesce p cols row xs
  | Case cs els ->
      (match eval_case p cols row cs with
       | Some r -> r
       | None -> eval_expr p cols row els)
  | Cast t inner ->
      (match eval_expr p cols row inner with
       | Error err -> Error err
       | Ok v -> p.cast_cell t v)
  | InList s items ->
      (match eval_expr p cols row s with
       | Error err -> Error err
       | Ok sv -> if p.is_null sv then Ok p.null_cell else eval_in p cols row sv false items)
  | IsNull inner ->
      (match eval_expr p cols row inner with
       | Error err -> Error err
       | Ok v -> Ok (p.mk_bool (p.is_null v)))
  | InParam _ name -> Error (p.unbound_param name p.env_names)
  | Now gr -> Error (p.unpinned_clock gr)
  | ApplyFn f args ->
      (match eval_args p cols row args with
       | Error err -> Error err
       | Ok vs -> p.apply_fn f vs)

(* `Coalesce`'s `go`: the first non-null value, `Null` when every one is null. *)
and eval_coalesce (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (xs:list (colexpr c op fn ty g)) : Tot (outcome c e) (decreases xs) =
  match xs with
  | [] -> Ok p.null_cell
  | x :: rest ->
      (match eval_expr p cols row x with
       | Error err -> Error err
       | Ok v -> if p.is_null v then eval_coalesce p cols row rest else Ok v)

(* `Case`'s `go`, up to the fall-through: `None` is production's `go [] -> evalExpr elseExpr`. *)
and eval_case (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (cs:list (colexpr c op fn ty g & colexpr c op fn ty g))
  : Tot (option (outcome c e)) (decreases cs) =
  match cs with
  | [] -> None
  | (w, t) :: rest ->
      (match eval_expr p cols row w with
       | Error err -> Some (Error err)
       | Ok v ->
           (match p.as_bool v with
            | Some true -> Some (eval_expr p cols row t)
            | _ -> eval_case p cols row rest))

(* `InList`'s `go sawNull`: SQL three-valued membership. *)
and eval_in (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (sv:c) (saw_null:bool) (items:list (colexpr c op fn ty g))
  : Tot (outcome c e) (decreases items) =
  match items with
  | [] -> Ok (if saw_null then p.null_cell else p.mk_bool false)
  | it :: rest ->
      (match eval_expr p cols row it with
       | Error err -> Error err
       | Ok iv ->
           if p.is_null iv then eval_in p cols row sv true rest
           else (match p.cells_equal sv iv with
                 | Some true -> Ok (p.mk_bool true)
                 | Some false -> eval_in p cols row sv saw_null rest
                 | None -> Error p.in_incompatible))

(* `ApplyFn`'s `evalArgs`: every argument, left to right, the first error wins. Production
   accumulates and reverses; this is the same list. *)
and eval_args (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (args:list (colexpr c op fn ty g))
  : Tot (outcome (list c) e) (decreases args) =
  match args with
  | [] -> Ok []
  | a :: rest ->
      (match eval_expr p cols row a with
       | Error err -> Error err
       | Ok v ->
           (match eval_args p cols row rest with
            | Error err -> Error err
            | Ok vs -> Ok (v :: vs)))

(* ======================================================================================
   3. The frame, `evalFilter`, `evalDerive`, `evalStep`.
   ====================================================================================== *)

(* F#: the private `Frame = { Cols: Schema; Rows: Cell list list }`. *)
type frame (c ty:Type0) = { cols : list (string & ty); rows : list (list c) }

let rec all_width (#c:Type0) (n:nat) (rows:list (list c)) : Tot bool =
  match rows with
  | [] -> true
  | r :: t -> len r = n && all_width n t

(* A well-formed frame: every row as wide as the schema. `toFrame` produces one by construction
   (it pads short columns with `Null`), and `ofFrame` throws on anything else. *)
let wf (#c #ty:Type0) (f:frame c ty) : Tot bool = all_width (len f.cols) f.rows

let rows_ok (#c #e:Type0) (n:nat) (r:outcome (list (list c)) e) : Tot bool =
  match r with Ok rs -> all_width n rs | Error _ -> true

let frame_ok (#c #ty #e:Type0) (r:outcome (frame c ty) e) : Tot bool =
  match r with Ok f -> wf f | Error _ -> true

(* F#: `evalFilter env cols rows pred` — a row survives on `Ok (Bool true)`, the first error wins. *)
let rec filter_rows (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (rows:list (list c){all_width (len cols) rows}) (pred:colexpr c op fn ty g)
  : Tot (r:outcome (list (list c)) e{rows_ok (len cols) r}) (decreases rows) =
  match rows with
  | [] -> Ok []
  | r :: rest ->
      (match eval_expr p cols r pred with
       | Error err -> Error err
       | Ok v ->
           (match p.as_bool v with
            | Some true ->
                (match filter_rows p cols rest pred with
                 | Error err -> Error err
                 | Ok rs -> Ok (r :: rs))
            | _ -> filter_rows p cols rest pred))

(* `evalDerive`'s `go`: the expression once per row, the first error wins. *)
let rec derive_cells (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (rows:list (list c){all_width (len cols) rows}) (x:colexpr c op fn ty g)
  : Tot (r:outcome (list c) e{match r with Ok vs -> len vs = len rows | Error _ -> true})
        (decreases rows) =
  match rows with
  | [] -> Ok []
  | r :: rest ->
      (match eval_expr p cols r x with
       | Error err -> Error err
       | Ok v ->
           (match derive_cells p cols rest x with
            | Error err -> Error err
            | Ok vs -> Ok (v :: vs)))

(* F#: `cells |> List.tryPick Cell.typeOf |> Option.defaultValue StringType` — `inferType`. *)
let rec first_type (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cells:list c)
  : Tot (option ty) =
  match cells with
  | [] -> None
  | v :: t -> (match p.type_of v with Some tt -> Some tt | None -> first_type p t)

let infer_type (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cells:list c) : Tot ty =
  match first_type p cells with Some t -> t | None -> p.string_type

(* F#: `f.Cols |> List.mapi (fun j (n, t) -> if j = i then n, ty else n, t)`. *)
let rec retype_at (#ty:Type0) (i:nat) (t':ty) (cols:list (string & ty))
  : Tot (list (string & ty)) (decreases cols) =
  match cols with
  | [] -> []
  | (n, t) :: rest -> if i = 0 then (n, t') :: rest else (n, t) :: retype_at (i - 1) t' rest

(* F#: `List.map2 (fun row c -> row |> List.mapi (fun j cell -> if j = i then c else cell))`. The
   refinement is `List.map2`'s own precondition (it throws on unequal lengths). *)
let rec zip_replace (#c:Type0) (i:nat) (rows:list (list c)) (cells:list c{len cells = len rows})
  : Tot (list (list c)) (decreases rows) =
  match rows, cells with
  | [], [] -> []
  | r :: rt, v :: vt -> set_at i v r :: zip_replace i rt vt

(* F#: `List.map2 (fun row c -> row @ [ c ])`. *)
let rec zip_append (#c:Type0) (rows:list (list c)) (cells:list c{len cells = len rows})
  : Tot (list (list c)) (decreases rows) =
  match rows, cells with
  | [], [] -> []
  | r :: rt, v :: vt -> app r [v] :: zip_append rt vt

let rec len_retype_at (#ty:Type0) (i:nat) (t':ty) (cols:list (string & ty))
  : Lemma (ensures len (retype_at i t' cols) == len cols) (decreases cols) =
  match cols with
  | [] -> ()
  | _ :: rest -> if i = 0 then () else len_retype_at (i - 1) t' rest

let rec width_replace (#c:Type0) (n:nat) (i:nat) (rows:list (list c)) (cells:list c{len cells = len rows})
  : Lemma (requires all_width n rows) (ensures all_width n (zip_replace i rows cells))
          (decreases rows) =
  match rows, cells with
  | [], [] -> ()
  | r :: rt, v :: vt -> len_set_at i v r; width_replace n i rt vt

let rec width_append (#c:Type0) (n:nat) (rows:list (list c)) (cells:list c{len cells = len rows})
  : Lemma (requires all_width n rows) (ensures all_width (n + 1) (zip_append rows cells))
          (decreases rows) =
  match rows, cells with
  | [], [] -> ()
  | r :: rt, v :: vt -> len_app r [v]; width_append n rt vt

(* A well-formed frame, as a type: what every step takes and every step returns. *)
type wframe (c ty:Type0) = f:frame c ty{wf f}

(* F#: `evalDerive env f name expr` — replace the named column in place, else append it. *)
let eval_derive (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (f:wframe c ty)
  (name:string) (x:colexpr c op fn ty g) : Tot (outcome (wframe c ty) e) =
  match derive_cells p f.cols f.rows x with
  | Error err -> Error err
  | Ok cells ->
      let t = infer_type p cells in
      (match index_of name f.cols with
       | Some i ->
           len_retype_at i t f.cols;
           width_replace (len f.cols) i f.rows cells;
           Ok ({ cols = retype_at i t f.cols; rows = zip_replace i f.rows cells })
       | None ->
           len_app f.cols [(name, t)];
           width_append (len f.cols) f.rows cells;
           Ok ({ cols = app f.cols [(name, t)]; rows = zip_append f.rows cells }))

(* The twelve verbs the model does not restate, and their contract: a well-formed frame in, a
   well-formed frame or a named error out. The contract is a LEMMA FIELD beside the function; it
   extracts to a function returning `unit`, which a host supplies as `fun _ _ -> ()`. *)
noeq type step_eval (c op fn ty g e p:Type0) = {
  step    : frame c ty -> transform c op fn ty g p -> outcome (frame c ty) e;
  step_wf : f:frame c ty -> t:transform c op fn ty g p
            -> Lemma (requires wf f) (ensures frame_ok (step f t));
}

(* F#: `evalStep resolve env f t`. *)
let eval_step (#c #op #fn #ty #g #e #p:Type0) (pr:prims c op fn ty g e) (other:step_eval c op fn ty g e p)
  (f:wframe c ty) (t:transform c op fn ty g p) : Tot (outcome (wframe c ty) e) =
  match t with
  | Filter pred ->
      (match filter_rows pr f.cols f.rows pred with
       | Error err -> Error err
       | Ok rs -> Ok ({ cols = f.cols; rows = rs }))
  | Derive name x -> eval_derive pr f name x
  | _ ->
      other.step_wf f t;
      (match other.step f t with
       | Error err -> Error err
       | Ok f' -> Ok f')

(* ======================================================================================
   4. The counted driver and the cost model.

      The driver is stated ONCE, generically — over any state type `s`, any step type `t`, any
      step function and any cost model — and every theorem about counting is proved there. The
      pipeline's instance (`counted`) is that driver at `s = wframe`, `ev = eval_step`,
      `cost = row_cost`. Keeping the heavy types out of the counting proofs is a proof-cost
      decision (README, Phase 154's cost note); it changes nothing about what is modelled, since
      production's `go` is exactly this loop with `evalStep` and `costOf` fixed.
   ====================================================================================== *)

(* F#: `evalPipelineWithInEnvCounted`'s `go f evaluated` — the cost is taken on the frame the step
   stands on, and added only when the step succeeds. *)
let rec run (#s #t #e:Type0) (ev:s -> t -> outcome s e) (cost:s -> t -> nat)
  (f:s) (evaluated:nat) (pl:list t) : Tot (outcome (s & nat) e) (decreases pl) =
  match pl with
  | [] -> Ok (f, evaluated)
  | step :: rest ->
      let k = cost f step in
      (match ev f step with
       | Error err -> Error err
       | Ok f' -> run ev cost f' (evaluated + k) rest)

(* F#: `costOf` — a `Filter` or a `Derive` is charged the rows alive where it stands. *)
let row_cost (#c #op #fn #ty #g #p:Type0) (f:wframe c ty) (t:transform c op fn ty g p) : Tot nat =
  match t with
  | Filter _ -> len f.rows
  | Derive _ _ -> len f.rows
  | _ -> 0

(* F#: `evalPipelineWithInEnvCounted resolve env pipeline input` — `go (toFrame input) 0 pipeline`. *)
let counted (#c #op #fn #ty #g #e #p:Type0) (pr:prims c op fn ty g e) (other:step_eval c op fn ty g e p)
  (pl:list (transform c op fn ty g p)) (f:wframe c ty) : Tot (outcome (wframe c ty & nat) e) =
  run (eval_step pr other) row_cost f 0 pl

(* F#: `Result.map fst`. *)
let map_fst (#a #b #e:Type0) (r:outcome (a & b) e) : Tot (outcome a e) =
  match r with Ok (x, _) -> Ok x | Error err -> Error err

(* F#: `evalPipelineWithInEnv resolve env pipeline input`. *)
let uncounted (#c #op #fn #ty #g #e #p:Type0) (pr:prims c op fn ty g e) (other:step_eval c op fn ty g e p)
  (pl:list (transform c op fn ty g p)) (f:wframe c ty) : Tot (outcome (wframe c ty) e) =
  map_fst (counted pr other pl f)

(* ======================================================================================
   5. The theorems — the driver's, generically, then the pipeline's.
   ====================================================================================== *)

(* Add `a` to a count, leaving an error alone. *)
let shift (#s #e:Type0) (a:nat) (r:outcome (s & nat) e) : Tot (outcome (s & nat) e) =
  match r with Ok (f, n) -> Ok (f, a + n) | Error err -> Error err

(* The accumulator is only an offset. *)
let rec run_shift (#s #t #e:Type0) (ev:s -> t -> outcome s e) (cost:s -> t -> nat)
  (f:s) (a:nat) (pl:list t)
  : Lemma (ensures run ev cost f a pl == shift a (run ev cost f 0 pl)) (decreases pl) =
  match pl with
  | [] -> ()
  | step :: rest ->
      (match ev f step with
       | Error _ -> ()
       | Ok f' ->
           run_shift ev cost f' (a + cost f step) rest;
           run_shift ev cost f' (cost f step) rest)

(* Running `p1 @ p2` is running `p1`, then `p2` from where it left off. *)
let rec run_app (#s #t #e:Type0) (ev:s -> t -> outcome s e) (cost:s -> t -> nat)
  (f:s) (a:nat) (p1 p2:list t)
  : Lemma (ensures run ev cost f a (app p1 p2) ==
                   (match run ev cost f a p1 with
                    | Ok (f1, n1) -> run ev cost f1 n1 p2
                    | Error err -> Error err))
          (decreases p1) =
  match p1 with
  | [] -> ()
  | step :: rest ->
      (match ev f step with
       | Error _ -> ()
       | Ok f' -> run_app ev cost f' (a + cost f step) rest p2)

(* The generic additivity: the count of `p1 @ p2` from zero is the count of `p1` plus the count of
   `p2` from the state `p1` left. *)
let run_additive (#s #t #e:Type0) (ev:s -> t -> outcome s e) (cost:s -> t -> nat)
  (f:s) (p1 p2:list t)
  : Lemma (run ev cost f 0 (app p1 p2) ==
           (match run ev cost f 0 p1 with
            | Ok (f1, n1) -> shift n1 (run ev cost f1 0 p2)
            | Error err -> Error err)) =
  run_app ev cost f 0 p1 p2;
  match run ev cost f 0 p1 with
  | Ok (f1, n1) -> run_shift ev cost f1 n1 p2
  | Error _ -> ()

(* The step at which a run fails, with the prefix before it, the rest after it, and the state it
   faced — `None` when every step succeeds. *)
let rec failure (#s #t #e:Type0) (ev:s -> t -> outcome s e) (f:s) (pl:list t)
  : Tot (option (list t & t & list t & s)) (decreases pl) =
  match pl with
  | [] -> None
  | st :: rest ->
      (match ev f st with
       | Error _ -> Some ([], st, rest, f)
       | Ok f' ->
           (match failure ev f' rest with
            | Some (pre, st', post, fk) -> Some (st :: pre, st', post, fk)
            | None -> None))

(* The generic totality characterisation: `Ok` exactly when no step fails, and an `Error` is the
   error of the one step `failure` names, on the state its prefix produced. *)
let rec run_total (#s #t #e:Type0) (ev:s -> t -> outcome s e) (cost:s -> t -> nat)
  (f:s) (pl:list t)
  : Lemma (ensures (match run ev cost f 0 pl with
                    | Ok _ -> None? (failure ev f pl)
                    | Error err ->
                        (match failure ev f pl with
                         | Some (pre, st, post, fk) ->
                             pl == app pre (st :: post) /\
                             (match run ev cost f 0 pre with
                              | Ok (g1, _) -> g1 == fk
                              | Error _ -> False) /\
                             ev fk st == Error err
                         | None -> False)))
          (decreases pl) =
  match pl with
  | [] -> ()
  | st :: rest ->
      (match ev f st with
       | Error _ -> ()
       | Ok f' ->
           run_shift ev cost f' (cost f st) rest;
           run_total ev cost f' rest;
           (match failure ev f' rest with
            | Some (pre, _, _, _) -> run_shift ev cost f' (cost f st) pre
            | None -> ()))

(* Every step satisfies `ok`. *)
let rec for_all (#t:Type0) (ok:t -> bool) (pl:list t) : Tot bool =
  match pl with
  | [] -> true
  | st :: rest -> ok st && for_all ok rest

(* Two cost models over the same run produce the same state, and when the first is pointwise at
   most `m` times the second on every step `ok` admits, the first total is at most `m` times the
   second. `ok` is the hypothesis on the pipeline; `bound` is where the instance discharges it. *)
let rec run_scaled (#s #t #e:Type0) (ev:s -> t -> outcome s e) (c1 c2:s -> t -> nat) (m:nat)
  (ok:t -> bool) (bound:(x:s -> st:t -> Lemma (requires ok st) (ensures c1 x st <= m * c2 x st)))
  (f:s) (pl:list t)
  : Lemma (requires for_all ok pl)
          (ensures (match run ev c1 f 0 pl, run ev c2 f 0 pl with
                    | Ok (f1, w), Ok (f2, n) -> f1 == f2 /\ w <= m * n
                    | Error e1, Error e2 -> e1 == e2
                    | _, _ -> False))
          (decreases pl) =
  match pl with
  | [] -> ()
  | st :: rest ->
      (match ev f st with
       | Error _ -> ()
       | Ok f' ->
           run_shift ev c1 f' (c1 f st) rest;
           run_shift ev c2 f' (c2 f st) rest;
           bound f st;
           run_scaled ev c1 c2 m ok bound f' rest)

(* `uncounted_is_projection` — `evalPipelineWithInEnv = counted |> Result.map fst`, production's
   definition restated as the identity it is. There is no second path to agree with. *)
let uncounted_is_projection (#c #op #fn #ty #g #e #p:Type0) (pr:prims c op fn ty g e)
  (other:step_eval c op fn ty g e p) (pl:list (transform c op fn ty g p)) (f:wframe c ty)
  : Lemma (uncounted pr other pl f == map_fst (counted pr other pl f)) = ()

(* `count_additive` — the count of `p1 @ p2` is the count of `p1` plus the count of `p2` from the
   frame `p1` left, and `p1 @ p2` fails exactly when one of the halves does. *)
let count_additive (#c #op #fn #ty #g #e #p:Type0) (pr:prims c op fn ty g e) (other:step_eval c op fn ty g e p)
  (p1 p2:list (transform c op fn ty g p)) (f:wframe c ty)
  : Lemma (counted pr other (app p1 p2) f ==
           (match counted pr other p1 f with
            | Ok (f1, n1) -> shift n1 (counted pr other p2 f1)
            | Error err -> Error err)) =
  run_additive (eval_step pr other) row_cost f p1 p2

(* `budget_monotone` — a pipeline that evaluates has every prefix evaluate, at no larger a count. *)
let budget_monotone (#c #op #fn #ty #g #e #p:Type0) (pr:prims c op fn ty g e) (other:step_eval c op fn ty g e p)
  (p1 p2:list (transform c op fn ty g p)) (f:wframe c ty)
  : Lemma (match counted pr other (app p1 p2) f with
           | Ok (_, n) -> (match counted pr other p1 f with
                           | Ok (_, n1) -> n1 <= n
                           | Error _ -> False)
           | Error _ -> True) =
  count_additive pr other p1 p2 f

(* `eval_total` — every pipeline on every well-formed frame reaches `Ok` with a well-formed frame
   (the type says so: `wframe`), or an `Error` that is exactly the named error of one step: the
   pipeline is `pre @ (s :: post)`, `pre` evaluates to the frame `fk`, and `s` fails on `fk` with
   that error. *)
let eval_total (#c #op #fn #ty #g #e #p:Type0) (pr:prims c op fn ty g e) (other:step_eval c op fn ty g e p)
  (pl:list (transform c op fn ty g p)) (f:wframe c ty)
  : Lemma (match counted pr other pl f with
           | Ok (f', _) -> wf f' /\ None? (failure (eval_step pr other) f pl)
           | Error err ->
               (match failure (eval_step pr other) f pl with
                | Some (pre, s, post, fk) ->
                    pl == app pre (s :: post) /\
                    (match counted pr other pre f with
                     | Ok (g1, _) -> g1 == fk
                     | Error _ -> False) /\
                    eval_step pr other fk s == Error err
                | None -> False)) =
  run_total (eval_step pr other) row_cost f pl

(* ======================================================================================
   6. The §21.8 bound, as a hypothesis on the pipeline.
   ====================================================================================== *)

(* The step is within `Limits.max_expr_nodes` — every expression it embeds, which is at most one. *)
let step_within (#c #op #fn #ty #g #p:Type0) (t:transform c op fn ty g p) : Tot bool =
  match t with
  | Filter x -> expr_nodes x <= Limits.max_expr_nodes
  | Derive _ x -> expr_nodes x <= Limits.max_expr_nodes
  | _ -> true

(* Every expression the pipeline embeds is within `Limits.max_expr_nodes`. A HYPOTHESIS: the
   evaluator checks nothing of the kind (see `README.md`, the unenforced-limit finding). *)
let within_expr_limit (#c #op #fn #ty #g #p:Type0) (pl:list (transform c op fn ty g p)) : Tot bool =
  for_all step_within pl

(* The node-weighted cost: rows times expression nodes, per evaluating step. *)
let node_cost (#c #op #fn #ty #g #p:Type0) (f:wframe c ty) (t:transform c op fn ty g p) : Tot nat =
  match t with
  | Filter x -> len f.rows * expr_nodes x
  | Derive _ x -> len f.rows * expr_nodes x
  | _ -> 0

let rec mul_le (a b d:nat) : Lemma (requires b <= d) (ensures a * b <= a * d) =
  if a = 0 then () else mul_le (a - 1) b d

let node_le_row (#c #op #fn #ty #g #p:Type0) (f:wframe c ty) (t:transform c op fn ty g p)
  : Lemma (requires step_within t) (ensures node_cost f t <= Limits.max_expr_nodes * row_cost f t) =
  match t with
  | Filter x -> mul_le (len f.rows) (expr_nodes x) Limits.max_expr_nodes
  | Derive _ x -> mul_le (len f.rows) (expr_nodes x) Limits.max_expr_nodes
  | _ -> ()

(* `budget_bounded` — under the §21.8 hypothesis, the node-weighted run and the counted run produce
   the same frame (or the same error), and the weighted cost is at most `max_expr_nodes` times the
   count. *)
let budget_bounded (#c #op #fn #ty #g #e #p:Type0) (pr:prims c op fn ty g e) (other:step_eval c op fn ty g e p)
  (pl:list (transform c op fn ty g p)) (f:wframe c ty)
  : Lemma (requires within_expr_limit pl)
          (ensures (match run (eval_step pr other) node_cost f 0 pl, counted pr other pl f with
                    | Ok (f1, w), Ok (f2, n) -> f1 == f2 /\ w <= Limits.max_expr_nodes * n
                    | Error e1, Error e2 -> e1 == e2
                    | _, _ -> False)) =
  run_scaled (eval_step pr other) node_cost row_cost Limits.max_expr_nodes step_within node_le_row f pl

(* ======================================================================================
   7. The instrumented reading: how many `evalExpr` calls one row costs.
   ====================================================================================== *)

(* The `evalExpr` invocations evaluating `x` on `row` makes, itself included, following every short
   circuit exactly as `eval_expr` takes it. *)
let rec expr_visits (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (x:colexpr c op fn ty g) : Tot nat (decreases x) =
  match x with
  | Col _ -> 1
  | Lit _ -> 1
  | Param _ -> 1
  | InParam _ _ -> 1
  | Now _ -> 1
  | Binary _ a b ->
      1 + expr_visits p cols row a
        + (match eval_expr p cols row a with Ok _ -> expr_visits p cols row b | Error _ -> 0)
  | Not a -> 1 + expr_visits p cols row a
  | Cast _ a -> 1 + expr_visits p cols row a
  | IsNull a -> 1 + expr_visits p cols row a
  | Coalesce xs -> 1 + coalesce_visits p cols row xs
  | Case cs els ->
      1 + case_visits p cols row cs
        + (match eval_case p cols row cs with Some _ -> 0 | None -> expr_visits p cols row els)
  | InList s items ->
      1 + expr_visits p cols row s
        + (match eval_expr p cols row s with
           | Ok sv -> if p.is_null sv then 0 else in_visits p cols row sv false items
           | Error _ -> 0)
  | ApplyFn _ args -> 1 + args_visits p cols row args

and coalesce_visits (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (xs:list (colexpr c op fn ty g)) : Tot nat (decreases xs) =
  match xs with
  | [] -> 0
  | x :: rest ->
      expr_visits p cols row x
      + (match eval_expr p cols row x with
         | Ok v -> if p.is_null v then coalesce_visits p cols row rest else 0
         | Error _ -> 0)

and case_visits (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (cs:list (colexpr c op fn ty g & colexpr c op fn ty g))
  : Tot nat (decreases cs) =
  match cs with
  | [] -> 0
  | (w, t) :: rest ->
      expr_visits p cols row w
      + (match eval_expr p cols row w with
         | Ok v -> (match p.as_bool v with
                    | Some true -> expr_visits p cols row t
                    | _ -> case_visits p cols row rest)
         | Error _ -> 0)

and in_visits (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (sv:c) (saw_null:bool) (items:list (colexpr c op fn ty g))
  : Tot nat (decreases items) =
  match items with
  | [] -> 0
  | it :: rest ->
      expr_visits p cols row it
      + (match eval_expr p cols row it with
         | Ok iv ->
             if p.is_null iv then in_visits p cols row sv true rest
             else (match p.cells_equal sv iv with
                   | Some false -> in_visits p cols row sv saw_null rest
                   | _ -> 0)
         | Error _ -> 0)

and args_visits (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (args:list (colexpr c op fn ty g)) : Tot nat (decreases args) =
  match args with
  | [] -> 0
  | a :: rest ->
      expr_visits p cols row a
      + (match eval_expr p cols row a with Ok _ -> args_visits p cols row rest | Error _ -> 0)

(* `visits_le_nodes` — one row's evaluation of `x` makes at most `expr_nodes x` calls. *)
let rec visits_le_nodes (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (x:colexpr c op fn ty g)
  : Lemma (ensures expr_visits p cols row x <= expr_nodes x) (decreases x) =
  match x with
  | Binary _ a b -> visits_le_nodes p cols row a; visits_le_nodes p cols row b
  | Not a -> visits_le_nodes p cols row a
  | Cast _ a -> visits_le_nodes p cols row a
  | IsNull a -> visits_le_nodes p cols row a
  | Coalesce xs -> coalesce_le p cols row xs
  | ApplyFn _ args -> args_le p cols row args
  | Case cs els -> case_le p cols row cs; visits_le_nodes p cols row els
  | InList s items ->
      visits_le_nodes p cols row s;
      (match eval_expr p cols row s with
       | Ok sv -> in_le p cols row sv false items
       | Error _ -> ())
  | _ -> ()

and coalesce_le (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (xs:list (colexpr c op fn ty g))
  : Lemma (ensures coalesce_visits p cols row xs <= list_nodes xs) (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest -> visits_le_nodes p cols row x; coalesce_le p cols row rest

and case_le (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (cs:list (colexpr c op fn ty g & colexpr c op fn ty g))
  : Lemma (ensures case_visits p cols row cs <= case_nodes cs) (decreases cs) =
  match cs with
  | [] -> ()
  | (w, t) :: rest -> visits_le_nodes p cols row w; visits_le_nodes p cols row t; case_le p cols row rest

and in_le (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (sv:c) (saw_null:bool) (items:list (colexpr c op fn ty g))
  : Lemma (ensures in_visits p cols row sv saw_null items <= list_nodes items) (decreases items) =
  match items with
  | [] -> ()
  | it :: rest ->
      visits_le_nodes p cols row it;
      in_le p cols row sv true rest;
      in_le p cols row sv saw_null rest

and args_le (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (row:row_of c (len cols)) (args:list (colexpr c op fn ty g))
  : Lemma (ensures args_visits p cols row args <= list_nodes args) (decreases args) =
  match args with
  | [] -> ()
  | a :: rest -> visits_le_nodes p cols row a; args_le p cols row rest

(* Over a step's rows: the visits every row's evaluation makes (an upper bound on a step that stops
   at its first error, which visits fewer) — at most rows times nodes, the weight `node_cost` charges. *)
let rec rows_visits (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (rows:list (list c){all_width (len cols) rows}) (x:colexpr c op fn ty g) : Tot nat (decreases rows) =
  match rows with
  | [] -> 0
  | r :: rest -> expr_visits p cols r x + rows_visits p cols rest x

let rec rows_visits_le (#c #op #fn #ty #g #e:Type0) (p:prims c op fn ty g e) (cols:list (string & ty))
  (rows:list (list c){all_width (len cols) rows}) (x:colexpr c op fn ty g)
  : Lemma (ensures rows_visits p cols rows x <= len rows * expr_nodes x) (decreases rows) =
  match rows with
  | [] -> ()
  | r :: rest -> visits_le_nodes p cols r x; rows_visits_le p cols rest x
