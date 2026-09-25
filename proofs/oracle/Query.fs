module Query
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


let rec mem : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( x  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals x y) || (mem x t))
     end))


let rec has_key = (fun ( k  :  Prims.string ) ( l  :  Prims.list<(Prims.string * 'a)> ) -> (match (l) with
| [] -> begin
     false
     end
| ((k', uu___))::t -> begin
     ((Prims.op_Equals k k') || (has_key k t))
     end))


let rec keys = (fun ( l  :  Prims.list<(Prims.string * 'a)> ) -> (match (l) with
| [] -> begin
     []
     end
| ((k, uu___))::t -> begin
     (k)::(keys t)
     end))


let rec distinct : Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((not ((mem x t))) && (distinct t))
     end))


let rec all_in : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) ( m  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((mem x m) && (all_in t m))
     end))

type column_type =
| IntType
| FloatType
| BoolType
| StringType
| DateType
| TimestampType


let uu___is_IntType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| IntType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FloatType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| FloatType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_BoolType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| BoolType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_StringType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| StringType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DateType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| DateType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TimestampType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| TimestampType -> begin
     true
     end
| uu___ -> begin
     false
     end))

type cell =
| Int of Prims.int
| Float of Prims.string
| Bool of Prims.bool
| Str of Prims.string
| Date of Prims.string
| Timestamp of Prims.string
| Null


let uu___is_Int : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Int (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Int__item___0 : cell  ->  Prims.int = (fun ( projectee  :  cell ) -> (match (projectee) with
| Int (_0) -> begin
     _0
     end))


let uu___is_Float : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Float (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Float__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Float (_0) -> begin
     _0
     end))


let uu___is_Bool : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Bool (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Bool__item___0 : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Bool (_0) -> begin
     _0
     end))


let uu___is_Str : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Str (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Str__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Str (_0) -> begin
     _0
     end))


let uu___is_Date : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Date (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Date__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Date (_0) -> begin
     _0
     end))


let uu___is_Timestamp : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Timestamp (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Timestamp__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Timestamp (_0) -> begin
     _0
     end))


let uu___is_Null : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Null -> begin
     true
     end
| uu___ -> begin
     false
     end))

type host_effect =
| Pure
| ReadsHost
| WritesHost


let uu___is_Pure : host_effect  ->  Prims.bool = (fun ( projectee  :  host_effect ) -> (match (projectee) with
| Pure -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ReadsHost : host_effect  ->  Prims.bool = (fun ( projectee  :  host_effect ) -> (match (projectee) with
| ReadsHost -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_WritesHost : host_effect  ->  Prims.bool = (fun ( projectee  :  host_effect ) -> (match (projectee) with
| WritesHost -> begin
     true
     end
| uu___ -> begin
     false
     end))

type determinism_source =
| Deterministic
| Clock
| Random
| Network


let uu___is_Deterministic : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| Deterministic -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Clock : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| Clock -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Random : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| Random -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Network : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| Network -> begin
     true
     end
| uu___ -> begin
     false
     end))

type effect_class = {host : host_effect; determinism : determinism_source}


let __proj__Mkeffect_class__item__host : effect_class  ->  host_effect = (fun ( projectee  :  effect_class ) -> (match (projectee) with
| {host = host; determinism = determinism} -> begin
     host
     end))


let __proj__Mkeffect_class__item__determinism : effect_class  ->  determinism_source = (fun ( projectee  :  effect_class ) -> (match (projectee) with
| {host = host; determinism = determinism} -> begin
     determinism
     end))

type query_param = {p_name : Prims.string; p_type : column_type; p_required : Prims.bool}


let __proj__Mkquery_param__item__p_name : query_param  ->  Prims.string = (fun ( projectee  :  query_param ) -> (match (projectee) with
| {p_name = p_name; p_type = p_type; p_required = p_required} -> begin
     p_name
     end))


let __proj__Mkquery_param__item__p_type : query_param  ->  column_type = (fun ( projectee  :  query_param ) -> (match (projectee) with
| {p_name = p_name; p_type = p_type; p_required = p_required} -> begin
     p_type
     end))


let __proj__Mkquery_param__item__p_required : query_param  ->  Prims.bool = (fun ( projectee  :  query_param ) -> (match (projectee) with
| {p_name = p_name; p_type = p_type; p_required = p_required} -> begin
     p_required
     end))

type query = {q_id : Prims.string; q_params : Prims.list<query_param>; q_schema : Prims.list<(Prims.string * column_type)>; q_effect : effect_class; q_source : Prims.string; q_timeout_ms : FStar_Pervasives_Native.option<Prims.int>; q_page_size : FStar_Pervasives_Native.option<Prims.int>}


let __proj__Mkquery__item__q_id : query  ->  Prims.string = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size} -> begin
     q_id
     end))


let __proj__Mkquery__item__q_params : query  ->  Prims.list<query_param> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size} -> begin
     q_params
     end))


let __proj__Mkquery__item__q_schema : query  ->  Prims.list<(Prims.string * column_type)> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size} -> begin
     q_schema
     end))


let __proj__Mkquery__item__q_effect : query  ->  effect_class = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size} -> begin
     q_effect
     end))


let __proj__Mkquery__item__q_source : query  ->  Prims.string = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size} -> begin
     q_source
     end))


let __proj__Mkquery__item__q_timeout_ms : query  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size} -> begin
     q_timeout_ms
     end))


let __proj__Mkquery__item__q_page_size : query  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size} -> begin
     q_page_size
     end))

type query_error =
| NoSuchQuery of Prims.string * Prims.list<Prims.string>
| DuplicateQuery of Prims.string
| UnknownParam of Prims.string * Prims.list<Prims.string>
| ParamTypeMismatch of Prims.string * column_type * column_type
| RequiredParamsUnbound of Prims.list<Prims.string>
| SourceNotResolved of Prims.string
| ExecutionFailed of Prims.string * Prims.list<Prims.string>
| Timeout


let uu___is_NoSuchQuery : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| NoSuchQuery (id, known) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NoSuchQuery__item__id : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| NoSuchQuery (id, known) -> begin
     id
     end))


let __proj__NoSuchQuery__item__known : query_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  query_error ) -> (match (projectee) with
| NoSuchQuery (id, known) -> begin
     known
     end))


let uu___is_DuplicateQuery : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| DuplicateQuery (id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateQuery__item__id : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| DuplicateQuery (id) -> begin
     id
     end))


let uu___is_UnknownParam : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| UnknownParam (name, declared) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownParam__item__name : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| UnknownParam (name, declared) -> begin
     name
     end))


let __proj__UnknownParam__item__declared : query_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  query_error ) -> (match (projectee) with
| UnknownParam (name, declared) -> begin
     declared
     end))


let uu___is_ParamTypeMismatch : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| ParamTypeMismatch (name, expected, got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ParamTypeMismatch__item__name : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| ParamTypeMismatch (name, expected, got) -> begin
     name
     end))


let __proj__ParamTypeMismatch__item__expected : query_error  ->  column_type = (fun ( projectee  :  query_error ) -> (match (projectee) with
| ParamTypeMismatch (name, expected, got) -> begin
     expected
     end))


let __proj__ParamTypeMismatch__item__got : query_error  ->  column_type = (fun ( projectee  :  query_error ) -> (match (projectee) with
| ParamTypeMismatch (name, expected, got) -> begin
     got
     end))


let uu___is_RequiredParamsUnbound : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| RequiredParamsUnbound (names) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RequiredParamsUnbound__item__names : query_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  query_error ) -> (match (projectee) with
| RequiredParamsUnbound (names) -> begin
     names
     end))


let uu___is_SourceNotResolved : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| SourceNotResolved (source) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SourceNotResolved__item__source : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| SourceNotResolved (source) -> begin
     source
     end))


let uu___is_ExecutionFailed : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| ExecutionFailed (detail, recoverable) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ExecutionFailed__item__detail : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| ExecutionFailed (detail, recoverable) -> begin
     detail
     end))


let __proj__ExecutionFailed__item__recoverable : query_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  query_error ) -> (match (projectee) with
| ExecutionFailed (detail, recoverable) -> begin
     recoverable
     end))


let uu___is_Timeout : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| Timeout -> begin
     true
     end
| uu___ -> begin
     false
     end))


type arguments = Prims.list<(Prims.string * cell)>


let cell_type : cell  ->  FStar_Pervasives_Native.option<column_type> = (fun ( c  :  cell ) -> (match (c) with
| Int (uu___) -> begin
     FStar_Pervasives_Native.Some (IntType)
     end
| Float (uu___) -> begin
     FStar_Pervasives_Native.Some (FloatType)
     end
| Bool (uu___) -> begin
     FStar_Pervasives_Native.Some (BoolType)
     end
| Str (uu___) -> begin
     FStar_Pervasives_Native.Some (StringType)
     end
| Date (uu___) -> begin
     FStar_Pervasives_Native.Some (DateType)
     end
| Timestamp (uu___) -> begin
     FStar_Pervasives_Native.Some (TimestampType)
     end
| Null -> begin
     FStar_Pervasives_Native.None
     end))


let determinism_tag : determinism_source  ->  Prims.string = (fun ( d  :  determinism_source ) -> (match (d) with
| Deterministic -> begin
     "deterministic"
     end
| Clock -> begin
     "clock"
     end
| Random -> begin
     "random"
     end
| Network -> begin
     "network"
     end))


let determinism_tag_of : query  ->  Prims.string = (fun ( q  :  query ) -> (determinism_tag q.q_effect.determinism))

type renderers = {render_int : Prims.int  ->  Prims.string; render_float : Prims.string  ->  Prims.string; hash : Prims.string  ->  Prims.string; name_le : Prims.string  ->  Prims.string  ->  Prims.bool; field : Prims.string  ->  Prims.string}


let __proj__Mkrenderers__item__render_int : renderers  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field} -> begin
     render_int
     end))


let __proj__Mkrenderers__item__render_float : renderers  ->  Prims.string  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field} -> begin
     render_float
     end))


let __proj__Mkrenderers__item__hash : renderers  ->  Prims.string  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field} -> begin
     hash
     end))


let __proj__Mkrenderers__item__name_le : renderers  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field} -> begin
     name_le
     end))


let __proj__Mkrenderers__item__field : renderers  ->  Prims.string  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field} -> begin
     field
     end))


let cell_tag : cell  ->  Prims.string = (fun ( c  :  cell ) -> (match (c) with
| Int (uu___) -> begin
     "i"
     end
| Float (uu___) -> begin
     "f"
     end
| Bool (uu___) -> begin
     "b"
     end
| Str (uu___) -> begin
     "s"
     end
| Date (uu___) -> begin
     "d"
     end
| Timestamp (uu___) -> begin
     "t"
     end
| Null -> begin
     "n"
     end))


let cell_payload : renderers  ->  cell  ->  Prims.string = (fun ( rn  :  renderers ) ( c  :  cell ) -> (match (c) with
| Int (v) -> begin
     (rn.render_int v)
     end
| Float (v) -> begin
     (rn.render_float v)
     end
| Bool (v) -> begin
      
if v then begin
     "1"
     end else begin
     "0"
     end
     end
| Str (v) -> begin
     v
     end
| Date (v) -> begin
     v
     end
| Timestamp (v) -> begin
     v
     end
| Null -> begin
     ""
     end))


let rec insert_arg : renderers  ->  (Prims.string * cell)  ->  arguments  ->  arguments = (fun ( rn  :  renderers ) ( x  :  (Prims.string * cell) ) ( l  :  arguments ) -> (match (l) with
| [] -> begin
     (x)::[]
     end
| (y)::t -> begin
     (

let uu___ = x
in (match (uu___) with
| (xn, uu___1) -> begin
     (

let uu___2 = y
in (match (uu___2) with
| (yn, uu___3) -> begin
      
if (rn.name_le xn yn) then begin
     (x)::l
     end else begin
     (y)::(insert_arg rn x t)
     end
     end))
     end))
     end))


let rec sort_args : renderers  ->  arguments  ->  arguments = (fun ( rn  :  renderers ) ( l  :  arguments ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (insert_arg rn x (sort_args rn t))
     end))


let rec arg_fields : renderers  ->  arguments  ->  Prims.list<Prims.string> = (fun ( rn  :  renderers ) ( l  :  arguments ) -> (match (l) with
| [] -> begin
     []
     end
| ((n, v))::t -> begin
     (n)::((cell_tag v))::((cell_payload rn v))::(arg_fields rn t)
     end))


let rec fields : renderers  ->  Prims.list<Prims.string>  ->  Prims.string = (fun ( rn  :  renderers ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     ""
     end
| (x)::t -> begin
     (Prims.strcat (rn.field x) (fields rn t))
     end))


let canonical : renderers  ->  arguments  ->  Prims.string = (fun ( rn  :  renderers ) ( l  :  arguments ) -> (fields rn (arg_fields rn l)))


let invocation_key : renderers  ->  query  ->  arguments  ->  Prims.string = (fun ( rn  :  renderers ) ( q  :  query ) ( a  :  arguments ) -> (Prims.strcat q.q_id (Prims.strcat "#" (rn.hash (canonical rn (sort_args rn a))))))


let rec find_param : Prims.string  ->  Prims.list<query_param>  ->  FStar_Pervasives_Native.option<query_param> = (fun ( name  :  Prims.string ) ( ps  :  Prims.list<query_param> ) -> (match (ps) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (p)::t -> begin
      
if (Prims.op_Equals p.p_name name) then begin
     FStar_Pervasives_Native.Some (p)
     end else begin
     (find_param name t)
     end
     end))


let rec param_names : Prims.list<query_param>  ->  Prims.list<Prims.string> = (fun ( ps  :  Prims.list<query_param> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
     (p.p_name)::(param_names t)
     end))


let rec check_args : Prims.list<query_param>  ->  Prims.list<Prims.string>  ->  arguments  ->  outcome<unit, query_error> = (fun ( ps  :  Prims.list<query_param> ) ( declared  :  Prims.list<Prims.string> ) ( a  :  arguments ) -> (match (a) with
| [] -> begin
     Ok (())
     end
| ((name, c))::rest -> begin
     (match ((find_param name ps)) with
| FStar_Pervasives_Native.None -> begin
     Error (UnknownParam (name, declared))
     end
| FStar_Pervasives_Native.Some (p) -> begin
     (match ((cell_type c)) with
| FStar_Pervasives_Native.None -> begin
     (check_args ps declared rest)
     end
| FStar_Pervasives_Native.Some (t) -> begin
      
if (Prims.op_Equals t p.p_type) then begin
     (check_args ps declared rest)
     end else begin
     Error (ParamTypeMismatch (name, p.p_type, t))
     end
     end)
     end)
     end))


let rec unbound_required : Prims.list<query_param>  ->  arguments  ->  Prims.list<Prims.string> = (fun ( ps  :  Prims.list<query_param> ) ( a  :  arguments ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
      
if (p.p_required && (not ((has_key p.p_name a)))) then begin
     (p.p_name)::(unbound_required t a)
     end else begin
     (unbound_required t a)
     end
     end))


let validate_params : query  ->  arguments  ->  outcome<unit, query_error> = (fun ( q  :  query ) ( a  :  arguments ) -> (match ((check_args q.q_params (param_names q.q_params) a)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((unbound_required q.q_params a)) with
| [] -> begin
     Ok (())
     end
| u -> begin
     Error (RequiredParamsUnbound (u))
     end)
     end))

type deferred<'a> =
| Pending
| Ready of 'a
| Failed of Prims.string


let uu___is_Pending = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Pending -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Ready = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Ready (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ready__item___0 = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Ready (_0) -> begin
     _0
     end))


let uu___is_Failed = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Failed (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Failed__item___0 = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Failed (_0) -> begin
     _0
     end))


let invoke = (fun ( q  :  query ) ( a  :  arguments ) ( resolve  :  query  ->  deferred<'v> ) -> (match ((validate_params q a)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((resolve q)) with
| Ready (r) -> begin
     Ok (Ready (r))
     end
| Pending -> begin
     Ok (Pending)
     end
| Failed (m) -> begin
     Error (ExecutionFailed (m, []))
     end)
     end))

type registry = {queries : Prims.list<query>}


let __proj__Mkregistry__item__queries : registry  ->  Prims.list<query> = (fun ( projectee  :  registry ) -> (match (projectee) with
| {queries = queries} -> begin
     queries
     end))


let empty : registry = {queries = []}


let rec find_query : Prims.string  ->  Prims.list<query>  ->  FStar_Pervasives_Native.option<query> = (fun ( id  :  Prims.string ) ( qs  :  Prims.list<query> ) -> (match (qs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (q)::t -> begin
      
if (Prims.op_Equals q.q_id id) then begin
     FStar_Pervasives_Native.Some (q)
     end else begin
     (find_query id t)
     end
     end))


let rec ids : Prims.list<query>  ->  Prims.list<Prims.string> = (fun ( qs  :  Prims.list<query> ) -> (match (qs) with
| [] -> begin
     []
     end
| (q)::t -> begin
     (q.q_id)::(ids t)
     end))


let register : query  ->  registry  ->  outcome<registry, query_error> = (fun ( q  :  query ) ( r  :  registry ) -> (match ((find_query q.q_id r.queries)) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     Error (DuplicateQuery (q.q_id))
     end
| FStar_Pervasives_Native.None -> begin
     Ok ({queries = (q)::r.queries})
     end))


let try_find_query : Prims.string  ->  registry  ->  FStar_Pervasives_Native.option<query> = (fun ( id  :  Prims.string ) ( r  :  registry ) -> (find_query id r.queries))


let enumerate : registry  ->  Prims.list<query> = (fun ( r  :  registry ) -> r.queries)


let dispatch = (fun ( r  :  registry ) ( id  :  Prims.string ) ( a  :  arguments ) ( resolve  :  query  ->  deferred<'v> ) -> (match ((find_query id r.queries)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchQuery (id, (ids r.queries)))
     end
| FStar_Pervasives_Native.Some (q) -> begin
     (invoke q a resolve)
     end))


let well_typed : Prims.list<query_param>  ->  (Prims.string * cell)  ->  Prims.bool = (fun ( ps  :  Prims.list<query_param> ) ( b  :  (Prims.string * cell) ) -> (

let uu___ = b
in (match (uu___) with
| (name, c) -> begin
     (match ((find_param name ps)) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (p) -> begin
     (match ((cell_type c)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| FStar_Pervasives_Native.Some (t) -> begin
     (Prims.op_Equals t p.p_type)
     end)
     end)
     end)))


let rec all_well_typed : Prims.list<query_param>  ->  arguments  ->  Prims.bool = (fun ( ps  :  Prims.list<query_param> ) ( a  :  arguments ) -> (match (a) with
| [] -> begin
     true
     end
| (b)::t -> begin
     ((well_typed ps b) && (all_well_typed ps t))
     end))


let rec mem_arg : (Prims.string * cell)  ->  arguments  ->  Prims.bool = (fun ( x  :  (Prims.string * cell) ) ( l  :  arguments ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals x y) || (mem_arg x t))
     end))


let rec sorted : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  arguments  ->  Prims.bool = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( l  :  arguments ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::tl -> begin
     (match (tl) with
| [] -> begin
     true
     end
| (y)::uu___ -> begin
     ((le (FStar_Pervasives_Native.fst x) (FStar_Pervasives_Native.fst y)) && (sorted le tl))
     end)
     end))


let rec nulls_of : Prims.list<query_param>  ->  arguments = (fun ( ps  :  Prims.list<query_param> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
     (((p.p_name), (Null)))::(nulls_of t)
     end))


let collision_params : Prims.list<query_param> = ({p_name = "a"; p_type = StringType; p_required = true})::({p_name = "b"; p_type = StringType; p_required = false})::[]


let collision_one : arguments = ((("a"), (Str ("1b=s2"))))::[]


let collision_two : arguments = ((("a"), (Str ("1"))))::((("b"), (Str ("2"))))::[]




