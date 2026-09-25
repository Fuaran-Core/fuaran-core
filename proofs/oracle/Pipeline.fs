module Pipeline
type outcome<'a, 'e> =
| Ok of 'a
| Error of 'e


let uu___is_Ok = (fun ( projectee  :  outcome<'a, 'e> ) -> (match (projectee) with
| Ok (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ok__item___0 = (fun ( projectee  :  outcome<'a, 'e> ) -> (match (projectee) with
| Ok (_0) -> begin
     _0
     end))


let uu___is_Error = (fun ( projectee  :  outcome<'a, 'e> ) -> (match (projectee) with
| Error (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Error__item___0 = (fun ( projectee  :  outcome<'a, 'e> ) -> (match (projectee) with
| Error (_0) -> begin
     _0
     end))


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let rec app = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     m
     end
| (h)::t -> begin
     (h)::(app t m)
     end))


let rec nth = (fun ( l  :  Prims.list<'a> ) ( i  :  Prims.nat ) -> (match (l) with
| (x)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     x
     end else begin
     (nth t (i - (Prims.parse_int "1")))
     end
     end))


let rec set_at = (fun ( i  :  Prims.nat ) ( v  :  'a ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     (v)::t
     end else begin
     (x)::(set_at (i - (Prims.parse_int "1")) v t)
     end
     end))


let rec names = (fun ( cols  :  Prims.list<(Prims.string * 'ty)> ) -> (match (cols) with
| [] -> begin
     []
     end
| ((n, uu___))::t -> begin
     (n)::(names t)
     end))


let rec index_of = (fun ( name  :  Prims.string ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) -> (match (cols) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, uu___))::t -> begin
      
if (Prims.op_Equals n name) then begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "0"))
     end else begin
     (match ((index_of name t)) with
| FStar_Pervasives_Native.Some (i) -> begin
     FStar_Pervasives_Native.Some ((i + (Prims.parse_int "1")))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
     end))

type colexpr<'c, 'op, 'fn, 'ty, 'g> =
| Col of Prims.string
| Lit of 'c
| Param of Prims.string
| Binary of 'op * colexpr<'c, 'op, 'fn, 'ty, 'g> * colexpr<'c, 'op, 'fn, 'ty, 'g>
| Not of colexpr<'c, 'op, 'fn, 'ty, 'g>
| Coalesce of Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>>
| Case of Prims.list<(colexpr<'c, 'op, 'fn, 'ty, 'g> * colexpr<'c, 'op, 'fn, 'ty, 'g>)> * colexpr<'c, 'op, 'fn, 'ty, 'g>
| Cast of 'ty * colexpr<'c, 'op, 'fn, 'ty, 'g>
| ApplyFn of 'fn * Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>>
| InList of colexpr<'c, 'op, 'fn, 'ty, 'g> * Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>>
| IsNull of colexpr<'c, 'op, 'fn, 'ty, 'g>
| InParam of colexpr<'c, 'op, 'fn, 'ty, 'g> * Prims.string
| Now of 'g


let uu___is_Col = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Col (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Col__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Col (_0) -> begin
     _0
     end))


let uu___is_Lit = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Lit (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Lit__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Lit (_0) -> begin
     _0
     end))


let uu___is_Param = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Param (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Param__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Param (_0) -> begin
     _0
     end))


let uu___is_Binary = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Binary (_0, _1, _2) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Binary__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Binary (_0, _1, _2) -> begin
     _0
     end))


let __proj__Binary__item___1 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Binary (_0, _1, _2) -> begin
     _1
     end))


let __proj__Binary__item___2 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Binary (_0, _1, _2) -> begin
     _2
     end))


let uu___is_Not = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Not (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Not__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Not (_0) -> begin
     _0
     end))


let uu___is_Coalesce = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Coalesce (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Coalesce__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Coalesce (_0) -> begin
     _0
     end))


let uu___is_Case = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Case (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Case__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Case (_0, _1) -> begin
     _0
     end))


let __proj__Case__item___1 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Case (_0, _1) -> begin
     _1
     end))


let uu___is_Cast = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Cast (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Cast__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Cast (_0, _1) -> begin
     _0
     end))


let __proj__Cast__item___1 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Cast (_0, _1) -> begin
     _1
     end))


let uu___is_ApplyFn = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| ApplyFn (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ApplyFn__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| ApplyFn (_0, _1) -> begin
     _0
     end))


let __proj__ApplyFn__item___1 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| ApplyFn (_0, _1) -> begin
     _1
     end))


let uu___is_InList = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| InList (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__InList__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| InList (_0, _1) -> begin
     _0
     end))


let __proj__InList__item___1 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| InList (_0, _1) -> begin
     _1
     end))


let uu___is_IsNull = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| IsNull (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__IsNull__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| IsNull (_0) -> begin
     _0
     end))


let uu___is_InParam = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| InParam (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__InParam__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| InParam (_0, _1) -> begin
     _0
     end))


let __proj__InParam__item___1 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| InParam (_0, _1) -> begin
     _1
     end))


let uu___is_Now = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Now (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Now__item___0 = (fun ( projectee  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (projectee) with
| Now (_0) -> begin
     _0
     end))

type transform<'c, 'op, 'fn, 'ty, 'g, 'p> =
| Filter of colexpr<'c, 'op, 'fn, 'ty, 'g>
| Project of 'p
| Derive of Prims.string * colexpr<'c, 'op, 'fn, 'ty, 'g>
| GroupBy of 'p
| Join of 'p
| Window of 'p
| Pivot of 'p
| Unpivot of 'p
| Sort of 'p
| Distinct
| Limit of 'p
| Union of 'p
| Intersect of 'p
| Except of 'p


let uu___is_Filter = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Filter (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Filter__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Filter (_0) -> begin
     _0
     end))


let uu___is_Project = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Project (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Project__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Project (_0) -> begin
     _0
     end))


let uu___is_Derive = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Derive (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Derive__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Derive (_0, _1) -> begin
     _0
     end))


let __proj__Derive__item___1 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Derive (_0, _1) -> begin
     _1
     end))


let uu___is_GroupBy = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| GroupBy (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__GroupBy__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| GroupBy (_0) -> begin
     _0
     end))


let uu___is_Join = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Join (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Join__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Join (_0) -> begin
     _0
     end))


let uu___is_Window = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Window (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Window__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Window (_0) -> begin
     _0
     end))


let uu___is_Pivot = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Pivot (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Pivot__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Pivot (_0) -> begin
     _0
     end))


let uu___is_Unpivot = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Unpivot (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Unpivot__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Unpivot (_0) -> begin
     _0
     end))


let uu___is_Sort = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Sort (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Sort__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Sort (_0) -> begin
     _0
     end))


let uu___is_Distinct = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Distinct -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Limit = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Limit (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Limit__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Limit (_0) -> begin
     _0
     end))


let uu___is_Union = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Union (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Union__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Union (_0) -> begin
     _0
     end))


let uu___is_Intersect = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Intersect (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Intersect__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Intersect (_0) -> begin
     _0
     end))


let uu___is_Except = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Except (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Except__item___0 = (fun ( projectee  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (projectee) with
| Except (_0) -> begin
     _0
     end))


let rec expr_nodes = (fun ( x  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (x) with
| Col (uu___) -> begin
     (Prims.parse_int "1")
     end
| Lit (uu___) -> begin
     (Prims.parse_int "1")
     end
| Param (uu___) -> begin
     (Prims.parse_int "1")
     end
| Now (uu___) -> begin
     (Prims.parse_int "1")
     end
| Binary (uu___, a, b) -> begin
     (((Prims.parse_int "1") + (expr_nodes a)) + (expr_nodes b))
     end
| Not (a) -> begin
     ((Prims.parse_int "1") + (expr_nodes a))
     end
| Cast (uu___, a) -> begin
     ((Prims.parse_int "1") + (expr_nodes a))
     end
| IsNull (a) -> begin
     ((Prims.parse_int "1") + (expr_nodes a))
     end
| InParam (a, uu___) -> begin
     ((Prims.parse_int "1") + (expr_nodes a))
     end
| Coalesce (xs) -> begin
     ((Prims.parse_int "1") + (list_nodes xs))
     end
| ApplyFn (uu___, xs) -> begin
     ((Prims.parse_int "1") + (list_nodes xs))
     end
| Case (cs, els) -> begin
     (((Prims.parse_int "1") + (case_nodes cs)) + (expr_nodes els))
     end
| InList (s, xs) -> begin
     (((Prims.parse_int "1") + (expr_nodes s)) + (list_nodes xs))
     end))
and list_nodes = (fun ( xs  :  Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>> ) -> (match (xs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (x)::t -> begin
     ((expr_nodes x) + (list_nodes t))
     end))
and case_nodes = (fun ( cs  :  Prims.list<(colexpr<'c, 'op, 'fn, 'ty, 'g> * colexpr<'c, 'op, 'fn, 'ty, 'g>)> ) -> (match (cs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| ((w, t))::r -> begin
     (((expr_nodes w) + (expr_nodes t)) + (case_nodes r))
     end))

type prims<'c, 'op, 'fn, 'ty, 'g, 'e> = {env_find : Prims.string  ->  FStar_Pervasives_Native.option<'c>; env_names : Prims.list<Prims.string>; binary : 'op  ->  'c  ->  'c  ->  outcome<'c, 'e>; as_bool : 'c  ->  FStar_Pervasives_Native.option<Prims.bool>; is_null : 'c  ->  Prims.bool; mk_bool : Prims.bool  ->  'c; null_cell : 'c; cast_cell : 'ty  ->  'c  ->  outcome<'c, 'e>; apply_fn : 'fn  ->  Prims.list<'c>  ->  outcome<'c, 'e>; cells_equal : 'c  ->  'c  ->  FStar_Pervasives_Native.option<Prims.bool>; type_of : 'c  ->  FStar_Pervasives_Native.option<'ty>; string_type : 'ty; unknown_column : Prims.string  ->  Prims.list<Prims.string>  ->  'e; unbound_param : Prims.string  ->  Prims.list<Prims.string>  ->  'e; unpinned_clock : 'g  ->  'e; not_non_bool : 'e; in_incompatible : 'e}


let __proj__Mkprims__item__env_find = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     env_find
     end))


let __proj__Mkprims__item__env_names = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     env_names
     end))


let __proj__Mkprims__item__binary = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     binary
     end))


let __proj__Mkprims__item__as_bool = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     as_bool
     end))


let __proj__Mkprims__item__is_null = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     is_null
     end))


let __proj__Mkprims__item__mk_bool = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     mk_bool
     end))


let __proj__Mkprims__item__null_cell = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     null_cell
     end))


let __proj__Mkprims__item__cast_cell = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     cast_cell
     end))


let __proj__Mkprims__item__apply_fn = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     apply_fn
     end))


let __proj__Mkprims__item__cells_equal = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     cells_equal
     end))


let __proj__Mkprims__item__type_of = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     type_of
     end))


let __proj__Mkprims__item__string_type = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     string_type
     end))


let __proj__Mkprims__item__unknown_column = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     unknown_column
     end))


let __proj__Mkprims__item__unbound_param = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     unbound_param
     end))


let __proj__Mkprims__item__unpinned_clock = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     unpinned_clock
     end))


let __proj__Mkprims__item__not_non_bool = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     not_non_bool
     end))


let __proj__Mkprims__item__in_incompatible = (fun ( projectee  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) -> (match (projectee) with
| {env_find = env_find; env_names = env_names; binary = binary; as_bool = as_bool; is_null = is_null; mk_bool = mk_bool; null_cell = null_cell; cast_cell = cast_cell; apply_fn = apply_fn; cells_equal = cells_equal; type_of = type_of; string_type = string_type; unknown_column = unknown_column; unbound_param = unbound_param; unpinned_clock = unpinned_clock; not_non_bool = not_non_bool; in_incompatible = in_incompatible} -> begin
     in_incompatible
     end))


type row_of<'c> = Prims.list<'c>


let rec eval_expr = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( x  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (x) with
| Col (name) -> begin
     (match ((index_of name cols)) with
| FStar_Pervasives_Native.Some (i) -> begin
     Ok ((nth row i))
     end
| FStar_Pervasives_Native.None -> begin
     Error ((p.unknown_column name (names cols)))
     end)
     end
| Lit (v) -> begin
     Ok (v)
     end
| Param (name) -> begin
     (match ((p.env_find name)) with
| FStar_Pervasives_Native.Some (v) -> begin
     Ok (v)
     end
| FStar_Pervasives_Native.None -> begin
     Error ((p.unbound_param name p.env_names))
     end)
     end
| Binary (o, a, b) -> begin
     (match ((eval_expr p cols row a)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (av) -> begin
     (match ((eval_expr p cols row b)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (bv) -> begin
     (p.binary o av bv)
     end)
     end)
     end
| Not (inner) -> begin
     (match ((eval_expr p cols row inner)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     (match ((p.as_bool v)) with
| FStar_Pervasives_Native.Some (b) -> begin
     Ok ((p.mk_bool (not (b))))
     end
| FStar_Pervasives_Native.None -> begin
      
if (p.is_null v) then begin
     Ok (p.null_cell)
     end else begin
     Error (p.not_non_bool)
     end
     end)
     end)
     end
| Coalesce (xs) -> begin
     (eval_coalesce p cols row xs)
     end
| Case (cs, els) -> begin
     (match ((eval_case p cols row cs)) with
| FStar_Pervasives_Native.Some (r) -> begin
     r
     end
| FStar_Pervasives_Native.None -> begin
     (eval_expr p cols row els)
     end)
     end
| Cast (t, inner) -> begin
     (match ((eval_expr p cols row inner)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     (p.cast_cell t v)
     end)
     end
| InList (s, items) -> begin
     (match ((eval_expr p cols row s)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (sv) -> begin
      
if (p.is_null sv) then begin
     Ok (p.null_cell)
     end else begin
     (eval_in p cols row sv false items)
     end
     end)
     end
| IsNull (inner) -> begin
     (match ((eval_expr p cols row inner)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     Ok ((p.mk_bool (p.is_null v)))
     end)
     end
| InParam (uu___, name) -> begin
     Error ((p.unbound_param name p.env_names))
     end
| Now (gr) -> begin
     Error ((p.unpinned_clock gr))
     end
| ApplyFn (f, args) -> begin
     (match ((eval_args p cols row args)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (vs) -> begin
     (p.apply_fn f vs)
     end)
     end))
and eval_coalesce = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( xs  :  Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>> ) -> (match (xs) with
| [] -> begin
     Ok (p.null_cell)
     end
| (x)::rest -> begin
     (match ((eval_expr p cols row x)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
      
if (p.is_null v) then begin
     (eval_coalesce p cols row rest)
     end else begin
     Ok (v)
     end
     end)
     end))
and eval_case = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( cs  :  Prims.list<(colexpr<'c, 'op, 'fn, 'ty, 'g> * colexpr<'c, 'op, 'fn, 'ty, 'g>)> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((w, t))::rest -> begin
     (match ((eval_expr p cols row w)) with
| Error (err) -> begin
     FStar_Pervasives_Native.Some (Error (err))
     end
| Ok (v) -> begin
     (match ((p.as_bool v)) with
| FStar_Pervasives_Native.Some (true) -> begin
     FStar_Pervasives_Native.Some ((eval_expr p cols row t))
     end
| uu___ -> begin
     (eval_case p cols row rest)
     end)
     end)
     end))
and eval_in = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( sv  :  'c ) ( saw_null  :  Prims.bool ) ( items  :  Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>> ) -> (match (items) with
| [] -> begin
     Ok ( 
if saw_null then begin
     p.null_cell
     end else begin
     (p.mk_bool false)
     end)
     end
| (it)::rest -> begin
     (match ((eval_expr p cols row it)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (iv) -> begin
      
if (p.is_null iv) then begin
     (eval_in p cols row sv true rest)
     end else begin
     (match ((p.cells_equal sv iv)) with
| FStar_Pervasives_Native.Some (true) -> begin
     Ok ((p.mk_bool true))
     end
| FStar_Pervasives_Native.Some (false) -> begin
     (eval_in p cols row sv saw_null rest)
     end
| FStar_Pervasives_Native.None -> begin
     Error (p.in_incompatible)
     end)
     end
     end)
     end))
and eval_args = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( args  :  Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>> ) -> (match (args) with
| [] -> begin
     Ok ([])
     end
| (a)::rest -> begin
     (match ((eval_expr p cols row a)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     (match ((eval_args p cols row rest)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (vs) -> begin
     Ok ((v)::vs)
     end)
     end)
     end))

type frame<'c, 'ty> = {cols : Prims.list<(Prims.string * 'ty)>; rows : Prims.list<Prims.list<'c>>}


let __proj__Mkframe__item__cols = (fun ( projectee  :  frame<'c, 'ty> ) -> (match (projectee) with
| {cols = cols; rows = rows} -> begin
     cols
     end))


let __proj__Mkframe__item__rows = (fun ( projectee  :  frame<'c, 'ty> ) -> (match (projectee) with
| {cols = cols; rows = rows} -> begin
     rows
     end))


let rec all_width = (fun ( n  :  Prims.nat ) ( rows  :  Prims.list<Prims.list<'c>> ) -> (match (rows) with
| [] -> begin
     true
     end
| (r)::t -> begin
     ((Prims.op_Equals (len r) n) && (all_width n t))
     end))


let wf = (fun ( f  :  frame<'c, 'ty> ) -> (all_width (len f.cols) f.rows))


let rows_ok = (fun ( n  :  Prims.nat ) ( r  :  outcome<Prims.list<Prims.list<'c>>, 'e> ) -> (match (r) with
| Ok (rs) -> begin
     (all_width n rs)
     end
| Error (uu___) -> begin
     true
     end))


let frame_ok = (fun ( r  :  outcome<frame<'c, 'ty>, 'e> ) -> (match (r) with
| Ok (f) -> begin
     (wf f)
     end
| Error (uu___) -> begin
     true
     end))


let rec filter_rows = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( rows  :  Prims.list<Prims.list<'c>> ) ( pred  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (rows) with
| [] -> begin
     Ok ([])
     end
| (r)::rest -> begin
     (match ((eval_expr p cols r pred)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     (match ((p.as_bool v)) with
| FStar_Pervasives_Native.Some (true) -> begin
     (match ((filter_rows p cols rest pred)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (rs) -> begin
     Ok ((r)::rs)
     end)
     end
| uu___ -> begin
     (filter_rows p cols rest pred)
     end)
     end)
     end))


let rec derive_cells = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( rows  :  Prims.list<Prims.list<'c>> ) ( x  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (rows) with
| [] -> begin
     Ok ([])
     end
| (r)::rest -> begin
     (match ((eval_expr p cols r x)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     (match ((derive_cells p cols rest x)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (vs) -> begin
     Ok ((v)::vs)
     end)
     end)
     end))


let rec first_type = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cells  :  Prims.list<'c> ) -> (match (cells) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (v)::t -> begin
     (match ((p.type_of v)) with
| FStar_Pervasives_Native.Some (tt) -> begin
     FStar_Pervasives_Native.Some (tt)
     end
| FStar_Pervasives_Native.None -> begin
     (first_type p t)
     end)
     end))


let infer_type = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cells  :  Prims.list<'c> ) -> (match ((first_type p cells)) with
| FStar_Pervasives_Native.Some (t) -> begin
     t
     end
| FStar_Pervasives_Native.None -> begin
     p.string_type
     end))


let rec retype_at = (fun ( i  :  Prims.nat ) ( t'  :  'ty ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) -> (match (cols) with
| [] -> begin
     []
     end
| ((n, t))::rest -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     (((n), (t')))::rest
     end else begin
     (((n), (t)))::(retype_at (i - (Prims.parse_int "1")) t' rest)
     end
     end))


let rec zip_replace = (fun ( i  :  Prims.nat ) ( rows  :  Prims.list<Prims.list<'c>> ) ( cells  :  Prims.list<'c> ) -> (match (((rows), (cells))) with
| ([], []) -> begin
     []
     end
| ((r)::rt, (v)::vt) -> begin
     ((set_at i v r))::(zip_replace i rt vt)
     end))


let rec zip_append = (fun ( rows  :  Prims.list<Prims.list<'c>> ) ( cells  :  Prims.list<'c> ) -> (match (((rows), (cells))) with
| ([], []) -> begin
     []
     end
| ((r)::rt, (v)::vt) -> begin
     ((app r ((v)::[])))::(zip_append rt vt)
     end))


type wframe<'c, 'ty> = frame<'c, 'ty>


let eval_derive = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( f  :  wframe<'c, 'ty> ) ( name  :  Prims.string ) ( x  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match ((derive_cells p f.cols f.rows x)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (cells) -> begin
     (

let t = (infer_type p cells)
in (match ((index_of name f.cols)) with
| FStar_Pervasives_Native.Some (i) -> begin
     Ok ({cols = (retype_at i t f.cols); rows = (zip_replace i f.rows cells)})
     end
| FStar_Pervasives_Native.None -> begin
     Ok ({cols = (app f.cols ((((name), (t)))::[])); rows = (zip_append f.rows cells)})
     end))
     end))

type step_eval<'c, 'op, 'fn, 'ty, 'g, 'e, 'p> = {step : frame<'c, 'ty>  ->  transform<'c, 'op, 'fn, 'ty, 'g, 'p>  ->  outcome<frame<'c, 'ty>, 'e>; step_wf : unit}


let __proj__Mkstep_eval__item__step = (fun ( projectee  :  step_eval<'c, 'op, 'fn, 'ty, 'g, 'e, 'p> ) -> (match (projectee) with
| {step = step; step_wf = step_wf} -> begin
     step
     end))


let eval_step = (fun ( pr  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( other  :  step_eval<'c, 'op, 'fn, 'ty, 'g, 'e, 'p> ) ( f  :  wframe<'c, 'ty> ) ( t  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (t) with
| Filter (pred) -> begin
     (match ((filter_rows pr f.cols f.rows pred)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (rs) -> begin
     Ok ({cols = f.cols; rows = rs})
     end)
     end
| Derive (name, x) -> begin
     (eval_derive pr f name x)
     end
| uu___ -> begin
     (match ((other.step f t)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (f') -> begin
     Ok (f')
     end)
     end))


let rec run = (fun ( ev  :  's  ->  't  ->  outcome<'s, 'e> ) ( cost  :  's  ->  't  ->  Prims.nat ) ( f  :  's ) ( evaluated  :  Prims.nat ) ( pl  :  Prims.list<'t> ) -> (match (pl) with
| [] -> begin
     Ok (((f), (evaluated)))
     end
| (step)::rest -> begin
     (

let k = (cost f step)
in (match ((ev f step)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (f') -> begin
     (run ev cost f' (evaluated + k) rest)
     end))
     end))


let row_cost = (fun ( f  :  wframe<'c, 'ty> ) ( t  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (t) with
| Filter (uu___) -> begin
     (len f.rows)
     end
| Derive (uu___, uu___1) -> begin
     (len f.rows)
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))


let counted = (fun ( pr  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( other  :  step_eval<'c, 'op, 'fn, 'ty, 'g, 'e, 'p> ) ( pl  :  Prims.list<transform<'c, 'op, 'fn, 'ty, 'g, 'p>> ) ( f  :  wframe<'c, 'ty> ) -> (run (eval_step pr other) row_cost f (Prims.parse_int "0") pl))


let map_fst = (fun ( r  :  outcome<('a * 'b), 'e> ) -> (match (r) with
| Ok (x, uu___) -> begin
     Ok (x)
     end
| Error (err) -> begin
     Error (err)
     end))


let uncounted = (fun ( pr  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( other  :  step_eval<'c, 'op, 'fn, 'ty, 'g, 'e, 'p> ) ( pl  :  Prims.list<transform<'c, 'op, 'fn, 'ty, 'g, 'p>> ) ( f  :  wframe<'c, 'ty> ) -> (map_fst (counted pr other pl f)))


let shift = (fun ( a  :  Prims.nat ) ( r  :  outcome<('s * Prims.nat), 'e> ) -> (match (r) with
| Ok (f, n) -> begin
     Ok (((f), ((a + n))))
     end
| Error (err) -> begin
     Error (err)
     end))


let rec failure = (fun ( ev  :  's  ->  't  ->  outcome<'s, 'e> ) ( f  :  's ) ( pl  :  Prims.list<'t> ) -> (match (pl) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (st)::rest -> begin
     (match ((ev f st)) with
| Error (uu___) -> begin
     FStar_Pervasives_Native.Some ((([]), (st), (rest), (f)))
     end
| Ok (f') -> begin
     (match ((failure ev f' rest)) with
| FStar_Pervasives_Native.Some (pre, st', post, fk) -> begin
     FStar_Pervasives_Native.Some ((((st)::pre), (st'), (post), (fk)))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end))


let rec for_all = (fun ( ok  :  't  ->  Prims.bool ) ( pl  :  Prims.list<'t> ) -> (match (pl) with
| [] -> begin
     true
     end
| (st)::rest -> begin
     ((ok st) && (for_all ok rest))
     end))


let step_within = (fun ( t  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (t) with
| Filter (x) -> begin
     ((expr_nodes x) <= Limits.max_expr_nodes)
     end
| Derive (uu___, x) -> begin
     ((expr_nodes x) <= Limits.max_expr_nodes)
     end
| uu___ -> begin
     true
     end))


let within_expr_limit = (fun ( pl  :  Prims.list<transform<'c, 'op, 'fn, 'ty, 'g, 'p>> ) -> (for_all step_within pl))


let node_cost = (fun ( f  :  wframe<'c, 'ty> ) ( t  :  transform<'c, 'op, 'fn, 'ty, 'g, 'p> ) -> (match (t) with
| Filter (x) -> begin
     ((len f.rows) * (expr_nodes x))
     end
| Derive (uu___, x) -> begin
     ((len f.rows) * (expr_nodes x))
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))


let rec expr_visits = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( x  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (x) with
| Col (uu___) -> begin
     (Prims.parse_int "1")
     end
| Lit (uu___) -> begin
     (Prims.parse_int "1")
     end
| Param (uu___) -> begin
     (Prims.parse_int "1")
     end
| InParam (uu___, uu___1) -> begin
     (Prims.parse_int "1")
     end
| Now (uu___) -> begin
     (Prims.parse_int "1")
     end
| Binary (uu___, a, b) -> begin
     (((Prims.parse_int "1") + (expr_visits p cols row a)) + (match ((eval_expr p cols row a)) with
| Ok (uu___1) -> begin
     (expr_visits p cols row b)
     end
| Error (uu___1) -> begin
     (Prims.parse_int "0")
     end))
     end
| Not (a) -> begin
     ((Prims.parse_int "1") + (expr_visits p cols row a))
     end
| Cast (uu___, a) -> begin
     ((Prims.parse_int "1") + (expr_visits p cols row a))
     end
| IsNull (a) -> begin
     ((Prims.parse_int "1") + (expr_visits p cols row a))
     end
| Coalesce (xs) -> begin
     ((Prims.parse_int "1") + (coalesce_visits p cols row xs))
     end
| Case (cs, els) -> begin
     (((Prims.parse_int "1") + (case_visits p cols row cs)) + (match ((eval_case p cols row cs)) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     (Prims.parse_int "0")
     end
| FStar_Pervasives_Native.None -> begin
     (expr_visits p cols row els)
     end))
     end
| InList (s, items) -> begin
     (((Prims.parse_int "1") + (expr_visits p cols row s)) + (match ((eval_expr p cols row s)) with
| Ok (sv) -> begin
      
if (p.is_null sv) then begin
     (Prims.parse_int "0")
     end else begin
     (in_visits p cols row sv false items)
     end
     end
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end))
     end
| ApplyFn (uu___, args) -> begin
     ((Prims.parse_int "1") + (args_visits p cols row args))
     end))
and coalesce_visits = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( xs  :  Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>> ) -> (match (xs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (x)::rest -> begin
     ((expr_visits p cols row x) + (match ((eval_expr p cols row x)) with
| Ok (v) -> begin
      
if (p.is_null v) then begin
     (coalesce_visits p cols row rest)
     end else begin
     (Prims.parse_int "0")
     end
     end
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end))
     end))
and case_visits = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( cs  :  Prims.list<(colexpr<'c, 'op, 'fn, 'ty, 'g> * colexpr<'c, 'op, 'fn, 'ty, 'g>)> ) -> (match (cs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| ((w, t))::rest -> begin
     ((expr_visits p cols row w) + (match ((eval_expr p cols row w)) with
| Ok (v) -> begin
     (match ((p.as_bool v)) with
| FStar_Pervasives_Native.Some (true) -> begin
     (expr_visits p cols row t)
     end
| uu___ -> begin
     (case_visits p cols row rest)
     end)
     end
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end))
     end))
and in_visits = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( sv  :  'c ) ( saw_null  :  Prims.bool ) ( items  :  Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>> ) -> (match (items) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (it)::rest -> begin
     ((expr_visits p cols row it) + (match ((eval_expr p cols row it)) with
| Ok (iv) -> begin
      
if (p.is_null iv) then begin
     (in_visits p cols row sv true rest)
     end else begin
     (match ((p.cells_equal sv iv)) with
| FStar_Pervasives_Native.Some (false) -> begin
     (in_visits p cols row sv saw_null rest)
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end)
     end
     end
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end))
     end))
and args_visits = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( row  :  row_of<'c> ) ( args  :  Prims.list<colexpr<'c, 'op, 'fn, 'ty, 'g>> ) -> (match (args) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (a)::rest -> begin
     ((expr_visits p cols row a) + (match ((eval_expr p cols row a)) with
| Ok (uu___) -> begin
     (args_visits p cols row rest)
     end
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end))
     end))


let rec rows_visits = (fun ( p  :  prims<'c, 'op, 'fn, 'ty, 'g, 'e> ) ( cols  :  Prims.list<(Prims.string * 'ty)> ) ( rows  :  Prims.list<Prims.list<'c>> ) ( x  :  colexpr<'c, 'op, 'fn, 'ty, 'g> ) -> (match (rows) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (r)::rest -> begin
     ((expr_visits p cols r x) + (rows_visits p cols rest x))
     end))




