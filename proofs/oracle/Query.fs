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


let rec all_in : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) ( m  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((ListAlgebra.mem x m) && (all_in t m))
     end))

type time_unit =
| Seconds
| Milliseconds
| Microseconds
| Nanoseconds


let uu___is_Seconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Seconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Milliseconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Milliseconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Microseconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Microseconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Nanoseconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Nanoseconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let unit_digits : time_unit  ->  Prims.int = (fun ( u  :  time_unit ) -> (match (u) with
| Seconds -> begin
     (Prims.parse_int "0")
     end
| Milliseconds -> begin
     (Prims.parse_int "3")
     end
| Microseconds -> begin
     (Prims.parse_int "6")
     end
| Nanoseconds -> begin
     (Prims.parse_int "9")
     end))


let unit_widens : time_unit  ->  time_unit  ->  Prims.bool = (fun ( from  :  time_unit ) ( target  :  time_unit ) -> ((unit_digits from) <= (unit_digits target)))

type column_type =
| IntType
| FloatType
| BoolType
| StringType
| DateType
| TimestampType of time_unit
| DecimalType


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
| TimestampType (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TimestampType__item___0 : column_type  ->  time_unit = (fun ( projectee  :  column_type ) -> (match (projectee) with
| TimestampType (_0) -> begin
     _0
     end))


let uu___is_DecimalType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| DecimalType -> begin
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
| Timestamp of time_unit * Prims.string
| Null
| Decimal of Prims.string


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
| Timestamp (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Timestamp__item___0 : cell  ->  time_unit = (fun ( projectee  :  cell ) -> (match (projectee) with
| Timestamp (_0, _1) -> begin
     _0
     end))


let __proj__Timestamp__item___1 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Timestamp (_0, _1) -> begin
     _1
     end))


let uu___is_Null : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Null -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Decimal : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Decimal (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Decimal__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Decimal (_0) -> begin
     _0
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

type determinism_source = {has_clock : Prims.bool; has_random : Prims.bool; has_network : Prims.bool}


let __proj__Mkdeterminism_source__item__has_clock : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| {has_clock = has_clock; has_random = has_random; has_network = has_network} -> begin
     has_clock
     end))


let __proj__Mkdeterminism_source__item__has_random : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| {has_clock = has_clock; has_random = has_random; has_network = has_network} -> begin
     has_random
     end))


let __proj__Mkdeterminism_source__item__has_network : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| {has_clock = has_clock; has_random = has_random; has_network = has_network} -> begin
     has_network
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

type predicate =
| EqualTo of Prims.string * cell
| GreaterThan of Prims.string * cell
| AtLeast of Prims.string * cell
| LessThan of Prims.string * cell
| AtMost of Prims.string * cell
| Contains of Prims.string * Prims.string
| IsNull of Prims.string
| IsNotNull of Prims.string


let uu___is_EqualTo : predicate  ->  Prims.bool = (fun ( projectee  :  predicate ) -> (match (projectee) with
| EqualTo (column, value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EqualTo__item__column : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| EqualTo (column, value) -> begin
     column
     end))


let __proj__EqualTo__item__value : predicate  ->  cell = (fun ( projectee  :  predicate ) -> (match (projectee) with
| EqualTo (column, value) -> begin
     value
     end))


let uu___is_GreaterThan : predicate  ->  Prims.bool = (fun ( projectee  :  predicate ) -> (match (projectee) with
| GreaterThan (column, value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__GreaterThan__item__column : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| GreaterThan (column, value) -> begin
     column
     end))


let __proj__GreaterThan__item__value : predicate  ->  cell = (fun ( projectee  :  predicate ) -> (match (projectee) with
| GreaterThan (column, value) -> begin
     value
     end))


let uu___is_AtLeast : predicate  ->  Prims.bool = (fun ( projectee  :  predicate ) -> (match (projectee) with
| AtLeast (column, value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AtLeast__item__column : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| AtLeast (column, value) -> begin
     column
     end))


let __proj__AtLeast__item__value : predicate  ->  cell = (fun ( projectee  :  predicate ) -> (match (projectee) with
| AtLeast (column, value) -> begin
     value
     end))


let uu___is_LessThan : predicate  ->  Prims.bool = (fun ( projectee  :  predicate ) -> (match (projectee) with
| LessThan (column, value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LessThan__item__column : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| LessThan (column, value) -> begin
     column
     end))


let __proj__LessThan__item__value : predicate  ->  cell = (fun ( projectee  :  predicate ) -> (match (projectee) with
| LessThan (column, value) -> begin
     value
     end))


let uu___is_AtMost : predicate  ->  Prims.bool = (fun ( projectee  :  predicate ) -> (match (projectee) with
| AtMost (column, value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AtMost__item__column : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| AtMost (column, value) -> begin
     column
     end))


let __proj__AtMost__item__value : predicate  ->  cell = (fun ( projectee  :  predicate ) -> (match (projectee) with
| AtMost (column, value) -> begin
     value
     end))


let uu___is_Contains : predicate  ->  Prims.bool = (fun ( projectee  :  predicate ) -> (match (projectee) with
| Contains (column, text) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Contains__item__column : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| Contains (column, text) -> begin
     column
     end))


let __proj__Contains__item__text : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| Contains (column, text) -> begin
     text
     end))


let uu___is_IsNull : predicate  ->  Prims.bool = (fun ( projectee  :  predicate ) -> (match (projectee) with
| IsNull (column) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__IsNull__item__column : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| IsNull (column) -> begin
     column
     end))


let uu___is_IsNotNull : predicate  ->  Prims.bool = (fun ( projectee  :  predicate ) -> (match (projectee) with
| IsNotNull (column) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__IsNotNull__item__column : predicate  ->  Prims.string = (fun ( projectee  :  predicate ) -> (match (projectee) with
| IsNotNull (column) -> begin
     column
     end))

type sort_direction =
| Ascending
| Descending


let uu___is_Ascending : sort_direction  ->  Prims.bool = (fun ( projectee  :  sort_direction ) -> (match (projectee) with
| Ascending -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Descending : sort_direction  ->  Prims.bool = (fun ( projectee  :  sort_direction ) -> (match (projectee) with
| Descending -> begin
     true
     end
| uu___ -> begin
     false
     end))

type sort_key = {k_column : Prims.string; k_direction : sort_direction}


let __proj__Mksort_key__item__k_column : sort_key  ->  Prims.string = (fun ( projectee  :  sort_key ) -> (match (projectee) with
| {k_column = k_column; k_direction = k_direction} -> begin
     k_column
     end))


let __proj__Mksort_key__item__k_direction : sort_key  ->  sort_direction = (fun ( projectee  :  sort_key ) -> (match (projectee) with
| {k_column = k_column; k_direction = k_direction} -> begin
     k_direction
     end))

type query = {q_id : Prims.string; q_params : Prims.list<query_param>; q_schema : Prims.list<(Prims.string * column_type)>; q_effect : effect_class; q_source : Prims.string; q_timeout_ms : FStar_Pervasives_Native.option<Prims.int>; q_page_size : FStar_Pervasives_Native.option<Prims.int>; q_where : Prims.list<predicate>; q_order_by : Prims.list<sort_key>}


let __proj__Mkquery__item__q_id : query  ->  Prims.string = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_id
     end))


let __proj__Mkquery__item__q_params : query  ->  Prims.list<query_param> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_params
     end))


let __proj__Mkquery__item__q_schema : query  ->  Prims.list<(Prims.string * column_type)> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_schema
     end))


let __proj__Mkquery__item__q_effect : query  ->  effect_class = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_effect
     end))


let __proj__Mkquery__item__q_source : query  ->  Prims.string = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_source
     end))


let __proj__Mkquery__item__q_timeout_ms : query  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_timeout_ms
     end))


let __proj__Mkquery__item__q_page_size : query  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_page_size
     end))


let __proj__Mkquery__item__q_where : query  ->  Prims.list<predicate> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_where
     end))


let __proj__Mkquery__item__q_order_by : query  ->  Prims.list<sort_key> = (fun ( projectee  :  query ) -> (match (projectee) with
| {q_id = q_id; q_params = q_params; q_schema = q_schema; q_effect = q_effect; q_source = q_source; q_timeout_ms = q_timeout_ms; q_page_size = q_page_size; q_where = q_where; q_order_by = q_order_by} -> begin
     q_order_by
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
| RequiredParamsNull of Prims.list<Prims.string>
| DuplicateParam of Prims.string
| UnknownColumn of Prims.string * Prims.list<Prims.string>
| PredicateTypeMismatch of Prims.string * column_type * column_type
| PredicateNotApplicable of Prims.string * Prims.string * column_type
| IllFormedLiteral of Prims.string * Prims.string
| DuplicateSortColumn of Prims.string


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


let uu___is_RequiredParamsNull : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| RequiredParamsNull (names) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RequiredParamsNull__item__names : query_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  query_error ) -> (match (projectee) with
| RequiredParamsNull (names) -> begin
     names
     end))


let uu___is_DuplicateParam : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| DuplicateParam (name) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateParam__item__name : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| DuplicateParam (name) -> begin
     name
     end))


let uu___is_UnknownColumn : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| UnknownColumn (name, declared) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownColumn__item__name : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| UnknownColumn (name, declared) -> begin
     name
     end))


let __proj__UnknownColumn__item__declared : query_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  query_error ) -> (match (projectee) with
| UnknownColumn (name, declared) -> begin
     declared
     end))


let uu___is_PredicateTypeMismatch : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| PredicateTypeMismatch (column, expected, got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PredicateTypeMismatch__item__column : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| PredicateTypeMismatch (column, expected, got) -> begin
     column
     end))


let __proj__PredicateTypeMismatch__item__expected : query_error  ->  column_type = (fun ( projectee  :  query_error ) -> (match (projectee) with
| PredicateTypeMismatch (column, expected, got) -> begin
     expected
     end))


let __proj__PredicateTypeMismatch__item__got : query_error  ->  column_type = (fun ( projectee  :  query_error ) -> (match (projectee) with
| PredicateTypeMismatch (column, expected, got) -> begin
     got
     end))


let uu___is_PredicateNotApplicable : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| PredicateNotApplicable (predicate1, column, column_type1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PredicateNotApplicable__item__predicate : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| PredicateNotApplicable (predicate1, column, column_type1) -> begin
     predicate1
     end))


let __proj__PredicateNotApplicable__item__column : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| PredicateNotApplicable (predicate1, column, column_type1) -> begin
     column
     end))


let __proj__PredicateNotApplicable__item__column_type : query_error  ->  column_type = (fun ( projectee  :  query_error ) -> (match (projectee) with
| PredicateNotApplicable (predicate1, column, column_type1) -> begin
     column_type1
     end))


let uu___is_IllFormedLiteral : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| IllFormedLiteral (column, reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__IllFormedLiteral__item__column : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| IllFormedLiteral (column, reason) -> begin
     column
     end))


let __proj__IllFormedLiteral__item__reason : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| IllFormedLiteral (column, reason) -> begin
     reason
     end))


let uu___is_DuplicateSortColumn : query_error  ->  Prims.bool = (fun ( projectee  :  query_error ) -> (match (projectee) with
| DuplicateSortColumn (column) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateSortColumn__item__column : query_error  ->  Prims.string = (fun ( projectee  :  query_error ) -> (match (projectee) with
| DuplicateSortColumn (column) -> begin
     column
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
| Timestamp (u, uu___) -> begin
     FStar_Pervasives_Native.Some (TimestampType (u))
     end
| Decimal (uu___) -> begin
     FStar_Pervasives_Native.Some (DecimalType)
     end
| Null -> begin
     FStar_Pervasives_Native.None
     end))


let determinism_tag : determinism_source  ->  Prims.string = (fun ( d  :  determinism_source ) ->  
if d.has_clock then begin
      
if d.has_random then begin
      
if d.has_network then begin
     "clock+random+network"
     end else begin
     "clock+random"
     end
     end else begin
      
if d.has_network then begin
     "clock+network"
     end else begin
     "clock"
     end
     end
     end else begin
      
if d.has_random then begin
      
if d.has_network then begin
     "random+network"
     end else begin
     "random"
     end
     end else begin
      
if d.has_network then begin
     "network"
     end else begin
     "deterministic"
     end
     end
     end)


let determinism_tag_of : query  ->  Prims.string = (fun ( q  :  query ) -> (determinism_tag q.q_effect.determinism))

type renderers = {render_int : Prims.int  ->  Prims.string; render_float : Prims.string  ->  Prims.string; hash : Prims.string  ->  Prims.string; name_le : Prims.string  ->  Prims.string  ->  Prims.bool; field : Prims.string  ->  Prims.string; render_where : Prims.list<predicate>  ->  Prims.string; render_order : Prims.list<sort_key>  ->  Prims.string}


let __proj__Mkrenderers__item__render_int : renderers  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field; render_where = render_where; render_order = render_order} -> begin
     render_int
     end))


let __proj__Mkrenderers__item__render_float : renderers  ->  Prims.string  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field; render_where = render_where; render_order = render_order} -> begin
     render_float
     end))


let __proj__Mkrenderers__item__hash : renderers  ->  Prims.string  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field; render_where = render_where; render_order = render_order} -> begin
     hash
     end))


let __proj__Mkrenderers__item__name_le : renderers  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field; render_where = render_where; render_order = render_order} -> begin
     name_le
     end))


let __proj__Mkrenderers__item__field : renderers  ->  Prims.string  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field; render_where = render_where; render_order = render_order} -> begin
     field
     end))


let __proj__Mkrenderers__item__render_where : renderers  ->  Prims.list<predicate>  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field; render_where = render_where; render_order = render_order} -> begin
     render_where
     end))


let __proj__Mkrenderers__item__render_order : renderers  ->  Prims.list<sort_key>  ->  Prims.string = (fun ( projectee  :  renderers ) -> (match (projectee) with
| {render_int = render_int; render_float = render_float; hash = hash; name_le = name_le; field = field; render_where = render_where; render_order = render_order} -> begin
     render_order
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
| Timestamp (uu___, uu___1) -> begin
     "t"
     end
| Decimal (uu___) -> begin
     "m"
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
| Timestamp (uu___, v) -> begin
     v
     end
| Decimal (v) -> begin
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


let order_fields : renderers  ->  Prims.list<sort_key>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( rn  :  renderers ) ( o  :  Prims.list<sort_key> ) ( rest  :  Prims.list<Prims.string> ) -> (match (o) with
| [] -> begin
     rest
     end
| (uu___)::uu___1 -> begin
     ("")::("o")::((rn.render_order o))::rest
     end))


let shape_fields : renderers  ->  Prims.list<predicate>  ->  Prims.list<sort_key>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( rn  :  renderers ) ( w  :  Prims.list<predicate> ) ( o  :  Prims.list<sort_key> ) ( rest  :  Prims.list<Prims.string> ) -> (match (w) with
| [] -> begin
     (order_fields rn o rest)
     end
| (uu___)::uu___1 -> begin
     ("")::("w")::((rn.render_where w))::(order_fields rn o rest)
     end))


let key_fields : renderers  ->  query  ->  arguments  ->  Prims.list<Prims.string> = (fun ( rn  :  renderers ) ( q  :  query ) ( l  :  arguments ) -> (shape_fields rn q.q_where q.q_order_by (arg_fields rn l)))


let canonical_key : renderers  ->  query  ->  arguments  ->  Prims.string = (fun ( rn  :  renderers ) ( q  :  query ) ( l  :  arguments ) -> (fields rn (key_fields rn q l)))


let invocation_key : renderers  ->  query  ->  arguments  ->  Prims.string = (fun ( rn  :  renderers ) ( q  :  query ) ( a  :  arguments ) -> (Prims.strcat q.q_id (Prims.strcat "#" (rn.hash (canonical_key rn q (sort_args rn a))))))


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


let widens : column_type  ->  column_type  ->  Prims.bool = (fun ( from  :  column_type ) ( target  :  column_type ) -> (match (((from), (target))) with
| (TimestampType (a), TimestampType (b)) -> begin
     (unit_widens a b)
     end
| uu___ -> begin
     (((Prims.op_Equals from target) || ((Prims.op_Equals from IntType) && (Prims.op_Equals target FloatType))) || ((Prims.op_Equals from IntType) && (Prims.op_Equals target DecimalType)))
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
      
if (widens t p.p_type) then begin
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


let rec has_value : Prims.string  ->  arguments  ->  Prims.bool = (fun ( k  :  Prims.string ) ( a  :  arguments ) -> (match (a) with
| [] -> begin
     false
     end
| ((k', c))::t -> begin
     (((Prims.op_Equals k k') && (not ((match (c) with
| Null -> begin
     true
     end
| uu___ -> begin
     false
     end)))) || (has_value k t))
     end))


let rec null_required : Prims.list<query_param>  ->  arguments  ->  Prims.list<Prims.string> = (fun ( ps  :  Prims.list<query_param> ) ( a  :  arguments ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
      
if ((p.p_required && (has_key p.p_name a)) && (not ((has_value p.p_name a)))) then begin
     (p.p_name)::(null_required t a)
     end else begin
     (null_required t a)
     end
     end))


let rec repeated : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( seen  :  Prims.list<Prims.string> ) ( ks  :  Prims.list<Prims.string> ) -> (match (ks) with
| [] -> begin
     []
     end
| (k)::t -> begin
      
if (ListAlgebra.mem k seen) then begin
     (k)::(repeated seen t)
     end else begin
     (repeated ((k)::seen) t)
     end
     end))


let validate_params : query  ->  arguments  ->  outcome<unit, query_error> = (fun ( q  :  query ) ( a  :  arguments ) -> (match ((repeated [] (keys a))) with
| (d)::uu___ -> begin
     Error (DuplicateParam (d))
     end
| [] -> begin
     (match ((check_args q.q_params (param_names q.q_params) a)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((unbound_required q.q_params a)) with
| [] -> begin
     (match ((null_required q.q_params a)) with
| [] -> begin
     Ok (())
     end
| n -> begin
     Error (RequiredParamsNull (n))
     end)
     end
| u -> begin
     Error (RequiredParamsUnbound (u))
     end)
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


let rec schema_type : Prims.string  ->  Prims.list<(Prims.string * column_type)>  ->  FStar_Pervasives_Native.option<column_type> = (fun ( c  :  Prims.string ) ( s  :  Prims.list<(Prims.string * column_type)> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, t))::r -> begin
      
if (Prims.op_Equals n c) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     (schema_type c r)
     end
     end))


let predicate_column : predicate  ->  Prims.string = (fun ( p  :  predicate ) -> (match (p) with
| EqualTo (c, uu___) -> begin
     c
     end
| GreaterThan (c, uu___) -> begin
     c
     end
| AtLeast (c, uu___) -> begin
     c
     end
| LessThan (c, uu___) -> begin
     c
     end
| AtMost (c, uu___) -> begin
     c
     end
| Contains (c, uu___) -> begin
     c
     end
| IsNull (c) -> begin
     c
     end
| IsNotNull (c) -> begin
     c
     end))


let predicate_literal : predicate  ->  FStar_Pervasives_Native.option<cell> = (fun ( p  :  predicate ) -> (match (p) with
| EqualTo (uu___, v) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| GreaterThan (uu___, v) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| AtLeast (uu___, v) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| LessThan (uu___, v) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| AtMost (uu___, v) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| Contains (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end
| IsNull (uu___) -> begin
     FStar_Pervasives_Native.None
     end
| IsNotNull (uu___) -> begin
     FStar_Pervasives_Native.None
     end))


let null_literal_reason : Prims.string = "a null literal compares with nothing; test for absence with isNull"


let predicate_fault : Prims.list<(Prims.string * column_type)>  ->  predicate  ->  FStar_Pervasives_Native.option<query_error> = (fun ( s  :  Prims.list<(Prims.string * column_type)> ) ( p  :  predicate ) -> (

let c = (predicate_column p)
in (match ((schema_type c s)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (UnknownColumn (c, (keys s)))
     end
| FStar_Pervasives_Native.Some (ty) -> begin
     (match (p) with
| Contains (uu___, uu___1) -> begin
      
if (Prims.op_Equals ty StringType) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (PredicateNotApplicable ("contains", c, ty))
     end
     end
| uu___ -> begin
     (match ((predicate_literal p)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (Null) -> begin
     FStar_Pervasives_Native.Some (IllFormedLiteral (c, null_literal_reason))
     end
| FStar_Pervasives_Native.Some (v) -> begin
     (match ((cell_type v)) with
| FStar_Pervasives_Native.Some (got) -> begin
      
if (match (((got), (ty))) with
| (TimestampType (a), TimestampType (b)) -> begin
     (unit_widens a b)
     end
| uu___1 -> begin
     (Prims.op_Equals got ty)
     end) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (PredicateTypeMismatch (c, ty, got))
     end
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end)
     end)))


let rec where_fault : Prims.list<(Prims.string * column_type)>  ->  Prims.list<predicate>  ->  FStar_Pervasives_Native.option<query_error> = (fun ( s  :  Prims.list<(Prims.string * column_type)> ) ( w  :  Prims.list<predicate> ) -> (match (w) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (p)::r -> begin
     (match ((predicate_fault s p)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (e)
     end
| FStar_Pervasives_Native.None -> begin
     (where_fault s r)
     end)
     end))


let rec order_fault : Prims.list<(Prims.string * column_type)>  ->  Prims.list<Prims.string>  ->  Prims.list<sort_key>  ->  FStar_Pervasives_Native.option<query_error> = (fun ( s  :  Prims.list<(Prims.string * column_type)> ) ( seen  :  Prims.list<Prims.string> ) ( o  :  Prims.list<sort_key> ) -> (match (o) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (k)::r -> begin
      
if (not ((ListAlgebra.mem k.k_column (keys s)))) then begin
     FStar_Pervasives_Native.Some (UnknownColumn (k.k_column, (keys s)))
     end else begin
      
if (ListAlgebra.mem k.k_column seen) then begin
     FStar_Pervasives_Native.Some (DuplicateSortColumn (k.k_column))
     end else begin
     (order_fault s ((k.k_column)::seen) r)
     end
     end
     end))


let admission_fault : query  ->  FStar_Pervasives_Native.option<query_error> = (fun ( q  :  query ) -> (match ((repeated [] (param_names q.q_params))) with
| (d)::uu___ -> begin
     FStar_Pervasives_Native.Some (DuplicateParam (d))
     end
| [] -> begin
     (match ((where_fault q.q_schema q.q_where)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (e)
     end
| FStar_Pervasives_Native.None -> begin
     (order_fault q.q_schema [] q.q_order_by)
     end)
     end))


let register : query  ->  registry  ->  outcome<registry, query_error> = (fun ( q  :  query ) ( r  :  registry ) -> (match ((find_query q.q_id r.queries)) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     Error (DuplicateQuery (q.q_id))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((admission_fault q)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     Ok ({queries = (q)::r.queries})
     end)
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
     (widens t p.p_type)
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


let unit_named : (Prims.string  ->  time_unit)  ->  cell  ->  Prims.bool = (fun ( uo  :  Prims.string  ->  time_unit ) ( c  :  cell ) -> (match (c) with
| Timestamp (u, s) -> begin
     (Prims.op_Equals u (uo s))
     end
| uu___ -> begin
     true
     end))


let rec units_named : (Prims.string  ->  time_unit)  ->  arguments  ->  Prims.bool = (fun ( uo  :  Prims.string  ->  time_unit ) ( a  :  arguments ) -> (match (a) with
| [] -> begin
     true
     end
| ((uu___, c))::t -> begin
     ((unit_named uo c) && (units_named uo t))
     end))


let rec nulls_of : Prims.list<query_param>  ->  arguments = (fun ( ps  :  Prims.list<query_param> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
     (((p.p_name), (Null)))::(nulls_of t)
     end))


let rec required_valued : Prims.list<query_param>  ->  arguments  ->  Prims.bool = (fun ( ps  :  Prims.list<query_param> ) ( a  :  arguments ) -> (match (ps) with
| [] -> begin
     true
     end
| (p)::t -> begin
     (((not (p.p_required)) || (has_value p.p_name a)) && (required_valued t a))
     end))


let rec required_names : Prims.list<query_param>  ->  Prims.list<Prims.string> = (fun ( ps  :  Prims.list<query_param> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
      
if p.p_required then begin
     (p.p_name)::(required_names t)
     end else begin
     (required_names t)
     end
     end))


let collision_params : Prims.list<query_param> = ({p_name = "a"; p_type = StringType; p_required = true})::({p_name = "b"; p_type = StringType; p_required = false})::[]


let collision_one : arguments = ((("a"), (Str ("1b=s2"))))::[]


let collision_two : arguments = ((("a"), (Str ("1"))))::((("b"), (Str ("2"))))::[]


let rec params_distinct : Prims.list<query>  ->  Prims.bool = (fun ( qs  :  Prims.list<query> ) -> (match (qs) with
| [] -> begin
     true
     end
| (q)::t -> begin
     ((match ((repeated [] (param_names q.q_params))) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) && (params_distinct t))
     end))


let page_fields : renderers  ->  FStar_Pervasives_Native.option<Prims.string>  ->  query  ->  arguments  ->  Prims.list<Prims.string> = (fun ( rn  :  renderers ) ( tok  :  FStar_Pervasives_Native.option<Prims.string> ) ( q  :  query ) ( l  :  arguments ) -> (match (tok) with
| FStar_Pervasives_Native.None -> begin
     (key_fields rn q l)
     end
| FStar_Pervasives_Native.Some (t) -> begin
     ("")::("p")::(t)::(key_fields rn q l)
     end))


let canonical_page : renderers  ->  FStar_Pervasives_Native.option<Prims.string>  ->  query  ->  arguments  ->  Prims.string = (fun ( rn  :  renderers ) ( tok  :  FStar_Pervasives_Native.option<Prims.string> ) ( q  :  query ) ( l  :  arguments ) -> (fields rn (page_fields rn tok q l)))


let invocation_key_page : renderers  ->  query  ->  arguments  ->  FStar_Pervasives_Native.option<Prims.string>  ->  Prims.string = (fun ( rn  :  renderers ) ( q  :  query ) ( a  :  arguments ) ( tok  :  FStar_Pervasives_Native.option<Prims.string> ) -> (Prims.strcat q.q_id (Prims.strcat "#" (rn.hash (canonical_page rn tok q (sort_args rn a))))))


let invoke_page = (fun ( q  :  query ) ( a  :  arguments ) ( tok  :  FStar_Pervasives_Native.option<Prims.string> ) ( resolve  :  query  ->  FStar_Pervasives_Native.option<Prims.string>  ->  deferred<'v> ) -> (invoke q a (fun ( q1  :  query ) -> (resolve q1 tok))))


let dispatch_page = (fun ( r  :  registry ) ( id  :  Prims.string ) ( a  :  arguments ) ( tok  :  FStar_Pervasives_Native.option<Prims.string> ) ( resolve  :  query  ->  FStar_Pervasives_Native.option<Prims.string>  ->  deferred<'v> ) -> (match ((find_query id r.queries)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchQuery (id, (ids r.queries)))
     end
| FStar_Pervasives_Native.Some (q) -> begin
     (invoke_page q a tok resolve)
     end))


let rec remove_query : Prims.string  ->  Prims.list<query>  ->  Prims.list<query> = (fun ( id  :  Prims.string ) ( qs  :  Prims.list<query> ) -> (match (qs) with
| [] -> begin
     []
     end
| (q)::t -> begin
      
if (Prims.op_Equals q.q_id id) then begin
     (remove_query id t)
     end else begin
     (q)::(remove_query id t)
     end
     end))


let unregister : Prims.string  ->  registry  ->  outcome<registry, query_error> = (fun ( id  :  Prims.string ) ( r  :  registry ) -> (match ((find_query id r.queries)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchQuery (id, (ids r.queries)))
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     Ok ({queries = (remove_query id r.queries)})
     end))


let rec keep_queries : Prims.list<Prims.string>  ->  Prims.list<query>  ->  Prims.list<query> = (fun ( keep  :  Prims.list<Prims.string> ) ( qs  :  Prims.list<query> ) -> (match (qs) with
| [] -> begin
     []
     end
| (q)::t -> begin
      
if (ListAlgebra.mem q.q_id keep) then begin
     (q)::(keep_queries keep t)
     end else begin
     (keep_queries keep t)
     end
     end))


let restrict : Prims.list<Prims.string>  ->  registry  ->  registry = (fun ( keep  :  Prims.list<Prims.string> ) ( r  :  registry ) -> {queries = (keep_queries keep r.queries)})


let rec first_shared : Prims.list<query>  ->  Prims.list<query>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( a  :  Prims.list<query> ) ( b  :  Prims.list<query> ) -> (match (b) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (q)::t -> begin
      
if (ListAlgebra.mem q.q_id (ids a)) then begin
     FStar_Pervasives_Native.Some (q.q_id)
     end else begin
     (first_shared a t)
     end
     end))


let rec app_queries : Prims.list<query>  ->  Prims.list<query>  ->  Prims.list<query> = (fun ( x  :  Prims.list<query> ) ( y  :  Prims.list<query> ) -> (match (x) with
| [] -> begin
     y
     end
| (h)::t -> begin
     (h)::(app_queries t y)
     end))


let union : registry  ->  registry  ->  outcome<registry, query_error> = (fun ( a  :  registry ) ( b  :  registry ) -> (match ((first_shared a.queries b.queries)) with
| FStar_Pervasives_Native.Some (id) -> begin
     Error (DuplicateQuery (id))
     end
| FStar_Pervasives_Native.None -> begin
     Ok ({queries = (app_queries a.queries b.queries)})
     end))


let twin_query : query = {q_id = "q"; q_params = ({p_name = "region"; p_type = StringType; p_required = true})::[]; q_schema = ((("n"), (StringType)))::[]; q_effect = {host = Pure; determinism = {has_clock = false; has_random = false; has_network = false}}; q_source = "src"; q_timeout_ms = FStar_Pervasives_Native.None; q_page_size = FStar_Pervasives_Native.None; q_where = []; q_order_by = []}


let twin_param : query_param = {p_name = "region"; p_type = StringType; p_required = true}


let twin_rn : renderers = {render_int = (fun ( uu___  :  Prims.int ) -> ""); render_float = (fun ( x  :  Prims.string ) -> x); hash = (fun ( x  :  Prims.string ) -> x); name_le = (fun ( uu___  :  Prims.string ) ( uu___1  :  Prims.string ) -> true); field = (fun ( x  :  Prims.string ) -> x); render_where = (fun ( uu___  :  Prims.list<predicate> ) -> "W"); render_order = (fun ( uu___  :  Prims.list<sort_key> ) -> "O")}


let twin_shaped : query = {q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = twin_query.q_schema; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = (Contains ("n", "eu"))::[]; q_order_by = ({k_column = "n"; k_direction = Descending})::[]}


let twins : Prims.list<Twin.twin> = ({Twin.tname = "validate-params-accepts-a-bound-required-param"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (validate_params twin_query (((("region"), (Str ("eu"))))::[])) (Ok (()))))})::({Twin.tname = "validate-params-refuses-an-unbound-required-param"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (validate_params twin_query []) (Error (RequiredParamsUnbound (("region")::[])))))})::({Twin.tname = "validate-params-refuses-a-type-mismatch"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (validate_params twin_query (((("region"), (Bool (true))))::[])) (Error (ParamTypeMismatch ("region", StringType, BoolType)))))})::({Twin.tname = "validate-params-widens-an-int-into-a-float-param"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (validate_params {q_id = twin_query.q_id; q_params = ({p_name = "region"; p_type = FloatType; p_required = true})::[]; q_schema = twin_query.q_schema; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = twin_query.q_where; q_order_by = twin_query.q_order_by} (((("region"), (Int ((Prims.parse_int "3")))))::[])) (Ok (()))))})::({Twin.tname = "validate-params-refuses-a-float-for-an-int-param"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (widens FloatType IntType) false))})::({Twin.tname = "validate-params-widens-a-seconds-timestamp-into-a-nanoseconds-param"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (validate_params {q_id = twin_query.q_id; q_params = ({p_name = "region"; p_type = TimestampType (Nanoseconds); p_required = true})::[]; q_schema = twin_query.q_schema; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = twin_query.q_where; q_order_by = twin_query.q_order_by} (((("region"), (Timestamp (Seconds, "2026-01-01T00:00:00Z"))))::[])) (Ok (()))) && (Prims.op_Equals (widens (TimestampType (Milliseconds)) (TimestampType (Seconds))) false)))})::({Twin.tname = "register-admits-a-coarser-timestamp-literal-and-refuses-a-finer"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (register {q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = ((("t"), (TimestampType (Milliseconds))))::[]; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = (AtLeast ("t", Timestamp (Seconds, "2026-01-01T00:00:00Z")))::[]; q_order_by = twin_query.q_order_by} empty) (Ok ({queries = ({q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = ((("t"), (TimestampType (Milliseconds))))::[]; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = (AtLeast ("t", Timestamp (Seconds, "2026-01-01T00:00:00Z")))::[]; q_order_by = twin_query.q_order_by})::[]}))) && (Prims.op_Equals (register {q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = ((("t"), (TimestampType (Milliseconds))))::[]; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = (AtLeast ("t", Timestamp (Microseconds, "2026-01-01T00:00:00.000001Z")))::[]; q_order_by = twin_query.q_order_by} empty) (Error (PredicateTypeMismatch ("t", TimestampType (Milliseconds), TimestampType (Microseconds)))))))})::({Twin.tname = "register-refuses-a-duplicate"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (register twin_query {queries = (twin_query)::[]}) (Error (DuplicateQuery ("q")))))})::({Twin.tname = "register-refuses-a-repeated-parameter-name"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (register {q_id = twin_query.q_id; q_params = (twin_param)::(twin_param)::[]; q_schema = twin_query.q_schema; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = twin_query.q_where; q_order_by = twin_query.q_order_by} empty) (Error (DuplicateParam ("region")))))})::({Twin.tname = "unregister-undoes-register"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (unregister "q" {queries = (twin_query)::[]}) (Ok (empty))))})::({Twin.tname = "the-first-page-adds-no-field"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (page_fields twin_rn FStar_Pervasives_Native.None twin_query (((("a"), (Str ("x"))))::[])) (("a")::("s")::("x")::[])))})::({Twin.tname = "a-later-page-leads-with-the-page-triple"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (page_fields twin_rn (FStar_Pervasives_Native.Some ("t")) twin_query []) (("")::("p")::("t")::[])))})::({Twin.tname = "a-shaped-declaration-leads-with-its-filter-then-its-order"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (page_fields twin_rn (FStar_Pervasives_Native.Some ("t")) twin_shaped (((("a"), (Str ("x"))))::[])) (("")::("p")::("t")::("")::("w")::("W")::("")::("o")::("O")::("a")::("s")::("x")::[])))})::({Twin.tname = "an-order-alone-adds-only-its-triple"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (key_fields twin_rn {q_id = twin_shaped.q_id; q_params = twin_shaped.q_params; q_schema = twin_shaped.q_schema; q_effect = twin_shaped.q_effect; q_source = twin_shaped.q_source; q_timeout_ms = twin_shaped.q_timeout_ms; q_page_size = twin_shaped.q_page_size; q_where = []; q_order_by = twin_shaped.q_order_by} []) (("")::("o")::("O")::[])))})::({Twin.tname = "register-admits-a-well-formed-filter-and-order"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (register twin_shaped empty) (Ok ({queries = (twin_shaped)::[]}))))})::({Twin.tname = "register-refuses-an-undeclared-filter-column"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (register {q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = twin_query.q_schema; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = (IsNull ("x"))::[]; q_order_by = twin_query.q_order_by} empty) (Error (UnknownColumn ("x", ("n")::[])))))})::({Twin.tname = "register-refuses-contains-on-a-column-that-is-not-a-string"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (register {q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = ((("n"), (IntType)))::[]; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = (Contains ("n", "1"))::[]; q_order_by = twin_query.q_order_by} empty) (Error (PredicateNotApplicable ("contains", "n", IntType)))))})::({Twin.tname = "register-refuses-a-null-literal"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (register {q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = twin_query.q_schema; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = (EqualTo ("n", Null))::[]; q_order_by = twin_query.q_order_by} empty) (Error (IllFormedLiteral ("n", null_literal_reason)))))})::({Twin.tname = "register-refuses-a-literal-of-another-type"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (register {q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = twin_query.q_schema; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = (AtLeast ("n", Int ((Prims.parse_int "3"))))::[]; q_order_by = twin_query.q_order_by} empty) (Error (PredicateTypeMismatch ("n", StringType, IntType)))))})::({Twin.tname = "register-refuses-an-order-naming-a-column-twice"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (register {q_id = twin_query.q_id; q_params = twin_query.q_params; q_schema = twin_query.q_schema; q_effect = twin_query.q_effect; q_source = twin_query.q_source; q_timeout_ms = twin_query.q_timeout_ms; q_page_size = twin_query.q_page_size; q_where = twin_query.q_where; q_order_by = ({k_column = "n"; k_direction = Ascending})::({k_column = "n"; k_direction = Descending})::[]} empty) (Error (DuplicateSortColumn ("n")))))})::[]


let column_tag : column_type  ->  Prims.string = (fun ( t  :  column_type ) -> (match (t) with
| IntType -> begin
     "int"
     end
| FloatType -> begin
     "float"
     end
| BoolType -> begin
     "bool"
     end
| StringType -> begin
     "string"
     end
| DateType -> begin
     "date"
     end
| TimestampType (Seconds) -> begin
     "timestamp"
     end
| TimestampType (Milliseconds) -> begin
     "timestamp_ms"
     end
| TimestampType (Microseconds) -> begin
     "timestamp_us"
     end
| TimestampType (Nanoseconds) -> begin
     "timestamp_ns"
     end
| DecimalType -> begin
     "decimal"
     end))


let column_of_tag : Prims.string  ->  FStar_Pervasives_Native.option<column_type> = (fun ( s  :  Prims.string ) ->  
if (Prims.op_Equals s "int") then begin
     FStar_Pervasives_Native.Some (IntType)
     end else begin
      
if (Prims.op_Equals s "float") then begin
     FStar_Pervasives_Native.Some (FloatType)
     end else begin
      
if (Prims.op_Equals s "bool") then begin
     FStar_Pervasives_Native.Some (BoolType)
     end else begin
      
if (Prims.op_Equals s "string") then begin
     FStar_Pervasives_Native.Some (StringType)
     end else begin
      
if (Prims.op_Equals s "date") then begin
     FStar_Pervasives_Native.Some (DateType)
     end else begin
      
if (Prims.op_Equals s "timestamp") then begin
     FStar_Pervasives_Native.Some (TimestampType (Seconds))
     end else begin
      
if (Prims.op_Equals s "decimal") then begin
     FStar_Pervasives_Native.Some (DecimalType)
     end else begin
      
if (Prims.op_Equals s "timestamp_ms") then begin
     FStar_Pervasives_Native.Some (TimestampType (Milliseconds))
     end else begin
      
if (Prims.op_Equals s "timestamp_us") then begin
     FStar_Pervasives_Native.Some (TimestampType (Microseconds))
     end else begin
      
if (Prims.op_Equals s "timestamp_ns") then begin
     FStar_Pervasives_Native.Some (TimestampType (Nanoseconds))
     end else begin
     FStar_Pervasives_Native.None
     end
     end
     end
     end
     end
     end
     end
     end
     end
     end)


let tagged_of = (fun ( look  :  Prims.string  ->  FStar_Pervasives_Native.option<'a> ) ( el  :  Capability.jval ) -> (match ((Capability.d_str el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (s) -> begin
     (match ((look s)) with
| FStar_Pervasives_Native.Some (v) -> begin
     Capability.Ok (v)
     end
| FStar_Pervasives_Native.None -> begin
     Capability.Error ((Capability.refuse Capability.UnknownTag))
     end)
     end))


let col_type_of_j : Capability.decoder<column_type> = (tagged_of column_of_tag)


let host_tag : host_effect  ->  Prims.string = (fun ( h  :  host_effect ) -> (match (h) with
| Pure -> begin
     "pure"
     end
| ReadsHost -> begin
     "readsHost"
     end
| WritesHost -> begin
     "writesHost"
     end))


let host_of_tag : Prims.string  ->  FStar_Pervasives_Native.option<host_effect> = (fun ( s  :  Prims.string ) ->  
if (Prims.op_Equals s "pure") then begin
     FStar_Pervasives_Native.Some (Pure)
     end else begin
      
if (Prims.op_Equals s "readsHost") then begin
     FStar_Pervasives_Native.Some (ReadsHost)
     end else begin
      
if (Prims.op_Equals s "writesHost") then begin
     FStar_Pervasives_Native.Some (WritesHost)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
     end)


let det_of_tag : Prims.string  ->  FStar_Pervasives_Native.option<determinism_source> = (fun ( s  :  Prims.string ) ->  
if (Prims.op_Equals s "deterministic") then begin
     FStar_Pervasives_Native.Some ({has_clock = false; has_random = false; has_network = false})
     end else begin
      
if (Prims.op_Equals s "network") then begin
     FStar_Pervasives_Native.Some ({has_clock = false; has_random = false; has_network = true})
     end else begin
      
if (Prims.op_Equals s "random") then begin
     FStar_Pervasives_Native.Some ({has_clock = false; has_random = true; has_network = false})
     end else begin
      
if (Prims.op_Equals s "random+network") then begin
     FStar_Pervasives_Native.Some ({has_clock = false; has_random = true; has_network = true})
     end else begin
      
if (Prims.op_Equals s "clock") then begin
     FStar_Pervasives_Native.Some ({has_clock = true; has_random = false; has_network = false})
     end else begin
      
if (Prims.op_Equals s "clock+network") then begin
     FStar_Pervasives_Native.Some ({has_clock = true; has_random = false; has_network = true})
     end else begin
      
if (Prims.op_Equals s "clock+random") then begin
     FStar_Pervasives_Native.Some ({has_clock = true; has_random = true; has_network = false})
     end else begin
      
if (Prims.op_Equals s "clock+random+network") then begin
     FStar_Pervasives_Native.Some ({has_clock = true; has_random = true; has_network = true})
     end else begin
     FStar_Pervasives_Native.None
     end
     end
     end
     end
     end
     end
     end
     end)


let effect_json : effect_class  ->  Capability.jval = (fun ( e  :  effect_class ) -> Capability.JObj (((("host"), (Capability.JStr ((host_tag e.host)))))::((("determinism"), (Capability.JStr ((determinism_tag e.determinism)))))::[]))


let effect_of_j : Capability.decoder<effect_class> = (Capability.both (Capability.field "host" (tagged_of host_of_tag)) (Capability.field "determinism" (tagged_of det_of_tag)) (fun ( h  :  host_effect ) ( d  :  determinism_source ) -> {host = h; determinism = d}))

type column_codec<'r> = {field_json : (Prims.string * column_type)  ->  Capability.jval; field_of : Capability.jval  ->  FStar_Pervasives_Native.option<(Prims.string * column_type)>; source_json : Prims.string  ->  Capability.jval; source_of : Capability.jval  ->  FStar_Pervasives_Native.option<Prims.string>; rows_json : 'r  ->  Capability.jval; rows_of : Capability.jval  ->  FStar_Pervasives_Native.option<'r>; lit_json : column_type  ->  cell  ->  Capability.jval; lit_of : Prims.string  ->  column_type  ->  Capability.jval  ->  FStar_Pervasives_Native.option<cell>}


let __proj__Mkcolumn_codec__item__field_json = (fun ( projectee  :  column_codec<'r> ) -> (match (projectee) with
| {field_json = field_json; field_of = field_of; source_json = source_json; source_of = source_of; rows_json = rows_json; rows_of = rows_of; lit_json = lit_json; lit_of = lit_of} -> begin
     field_json
     end))


let __proj__Mkcolumn_codec__item__field_of = (fun ( projectee  :  column_codec<'r> ) -> (match (projectee) with
| {field_json = field_json; field_of = field_of; source_json = source_json; source_of = source_of; rows_json = rows_json; rows_of = rows_of; lit_json = lit_json; lit_of = lit_of} -> begin
     field_of
     end))


let __proj__Mkcolumn_codec__item__source_json = (fun ( projectee  :  column_codec<'r> ) -> (match (projectee) with
| {field_json = field_json; field_of = field_of; source_json = source_json; source_of = source_of; rows_json = rows_json; rows_of = rows_of; lit_json = lit_json; lit_of = lit_of} -> begin
     source_json
     end))


let __proj__Mkcolumn_codec__item__source_of = (fun ( projectee  :  column_codec<'r> ) -> (match (projectee) with
| {field_json = field_json; field_of = field_of; source_json = source_json; source_of = source_of; rows_json = rows_json; rows_of = rows_of; lit_json = lit_json; lit_of = lit_of} -> begin
     source_of
     end))


let __proj__Mkcolumn_codec__item__rows_json = (fun ( projectee  :  column_codec<'r> ) -> (match (projectee) with
| {field_json = field_json; field_of = field_of; source_json = source_json; source_of = source_of; rows_json = rows_json; rows_of = rows_of; lit_json = lit_json; lit_of = lit_of} -> begin
     rows_json
     end))


let __proj__Mkcolumn_codec__item__rows_of = (fun ( projectee  :  column_codec<'r> ) -> (match (projectee) with
| {field_json = field_json; field_of = field_of; source_json = source_json; source_of = source_of; rows_json = rows_json; rows_of = rows_of; lit_json = lit_json; lit_of = lit_of} -> begin
     rows_of
     end))


let __proj__Mkcolumn_codec__item__lit_json = (fun ( projectee  :  column_codec<'r> ) -> (match (projectee) with
| {field_json = field_json; field_of = field_of; source_json = source_json; source_of = source_of; rows_json = rows_json; rows_of = rows_of; lit_json = lit_json; lit_of = lit_of} -> begin
     lit_json
     end))


let __proj__Mkcolumn_codec__item__lit_of = (fun ( projectee  :  column_codec<'r> ) -> (match (projectee) with
| {field_json = field_json; field_of = field_of; source_json = source_json; source_of = source_of; rows_json = rows_json; rows_of = rows_of; lit_json = lit_json; lit_of = lit_of} -> begin
     lit_of
     end))


let column_read = (fun ( rd  :  Capability.jval  ->  FStar_Pervasives_Native.option<'a> ) ( el  :  Capability.jval ) -> (match ((rd el)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Capability.Ok (x)
     end
| FStar_Pervasives_Native.None -> begin
     Capability.Error ((Capability.refuse Capability.OutOfRange))
     end))


let param_json : query_param  ->  Capability.jval = (fun ( p  :  query_param ) -> Capability.JObj (((("name"), (Capability.JStr (p.p_name))))::((("type"), (Capability.JStr ((column_tag p.p_type)))))::((("required"), (Capability.JBool (p.p_required))))::[]))


let param_of_j : Capability.decoder<query_param> = (Capability.both (Capability.field "name" Capability.d_str) (Capability.both (Capability.field "type" col_type_of_j) (Capability.field "required" Capability.d_bool) (fun ( t  :  column_type ) ( r  :  Prims.bool ) -> ((t), (r)))) (fun ( n  :  Prims.string ) ( tr  :  (column_type * Prims.bool) ) -> {p_name = n; p_type = (FStar_Pervasives_Native.fst tr); p_required = (FStar_Pervasives_Native.snd tr)}))


let predicate_tag : predicate  ->  Prims.string = (fun ( p  :  predicate ) -> (match (p) with
| EqualTo (uu___, uu___1) -> begin
     "equalTo"
     end
| GreaterThan (uu___, uu___1) -> begin
     "greaterThan"
     end
| AtLeast (uu___, uu___1) -> begin
     "atLeast"
     end
| LessThan (uu___, uu___1) -> begin
     "lessThan"
     end
| AtMost (uu___, uu___1) -> begin
     "atMost"
     end
| Contains (uu___, uu___1) -> begin
     "contains"
     end
| IsNull (uu___) -> begin
     "isNull"
     end
| IsNotNull (uu___) -> begin
     "isNotNull"
     end))


let predicate_json = (fun ( cc  :  column_codec<'r> ) ( p  :  predicate ) -> (

let col = (predicate_column p)
in (match (p) with
| Contains (uu___, text) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("contains"))))::((("column"), (Capability.JStr (col))))::((("text"), (Capability.JStr (text))))::[])
     end
| IsNull (uu___) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("isNull"))))::((("column"), (Capability.JStr (col))))::[])
     end
| IsNotNull (uu___) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("isNotNull"))))::((("column"), (Capability.JStr (col))))::[])
     end
| uu___ -> begin
     (match ((predicate_literal p)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (

let ty = (match ((cell_type v)) with
| FStar_Pervasives_Native.Some (t) -> begin
     t
     end
| FStar_Pervasives_Native.None -> begin
     StringType
     end)
in Capability.JObj (((("$type"), (Capability.JStr ((predicate_tag p)))))::((("column"), (Capability.JStr (col))))::((("type"), (Capability.JStr ((column_tag ty)))))::((("value"), ((cc.lit_json ty v))))::[]))
     end
| FStar_Pervasives_Native.None -> begin
     Capability.JObj (((("$type"), (Capability.JStr ((predicate_tag p)))))::((("column"), (Capability.JStr (col))))::[])
     end)
     end)))


let compared = (fun ( cc  :  column_codec<'r> ) ( make  :  Prims.string  ->  cell  ->  predicate ) ( el  :  Capability.jval ) -> (match ((Capability.field "column" Capability.d_str el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (c) -> begin
     (match ((Capability.field "type" col_type_of_j el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (ty) -> begin
     (match ((Capability.field "value" (column_read (cc.lit_of c ty)) el)) with
| Capability.Ok (v) -> begin
     Capability.Ok ((make c v))
     end
| Capability.Error (e) -> begin
     Capability.Error (e)
     end)
     end)
     end))


let predicate_cases = (fun ( cc  :  column_codec<'r> ) -> ((("equalTo"), ((compared cc (fun ( uu___  :  Prims.string ) ( uu___1  :  cell ) -> EqualTo (uu___, uu___1))))))::((("greaterThan"), ((compared cc (fun ( uu___  :  Prims.string ) ( uu___1  :  cell ) -> GreaterThan (uu___, uu___1))))))::((("atLeast"), ((compared cc (fun ( uu___  :  Prims.string ) ( uu___1  :  cell ) -> AtLeast (uu___, uu___1))))))::((("lessThan"), ((compared cc (fun ( uu___  :  Prims.string ) ( uu___1  :  cell ) -> LessThan (uu___, uu___1))))))::((("atMost"), ((compared cc (fun ( uu___  :  Prims.string ) ( uu___1  :  cell ) -> AtMost (uu___, uu___1))))))::((("contains"), ((Capability.both (Capability.field "column" Capability.d_str) (Capability.field "text" Capability.d_str) (fun ( c  :  Prims.string ) ( t  :  Prims.string ) -> Contains (c, t))))))::((("isNull"), ((Capability.d_map (fun ( uu___  :  Prims.string ) -> IsNull (uu___)) (Capability.field "column" Capability.d_str)))))::((("isNotNull"), ((Capability.d_map (fun ( uu___  :  Prims.string ) -> IsNotNull (uu___)) (Capability.field "column" Capability.d_str)))))::[])


let predicate_of_j = (fun ( cc  :  column_codec<'r> ) -> (Capability.tag_dispatch (predicate_cases cc)))


let direction_tag : sort_direction  ->  Prims.string = (fun ( d  :  sort_direction ) -> (match (d) with
| Ascending -> begin
     "ascending"
     end
| Descending -> begin
     "descending"
     end))


let direction_of_tag : Prims.string  ->  FStar_Pervasives_Native.option<sort_direction> = (fun ( s  :  Prims.string ) ->  
if (Prims.op_Equals s "ascending") then begin
     FStar_Pervasives_Native.Some (Ascending)
     end else begin
      
if (Prims.op_Equals s "descending") then begin
     FStar_Pervasives_Native.Some (Descending)
     end else begin
     FStar_Pervasives_Native.None
     end
     end)


let sort_key_json : sort_key  ->  Capability.jval = (fun ( k  :  sort_key ) -> Capability.JObj (((("column"), (Capability.JStr (k.k_column))))::((("direction"), (Capability.JStr ((direction_tag k.k_direction)))))::[]))


let sort_key_of_j : Capability.decoder<sort_key> = (Capability.both (Capability.field "column" Capability.d_str) (Capability.field "direction" (tagged_of direction_of_tag)) (fun ( c  :  Prims.string ) ( d  :  sort_direction ) -> {k_column = c; k_direction = d}))


let literal_carried : predicate  ->  Prims.bool = (fun ( p  :  predicate ) -> (match ((predicate_literal p)) with
| FStar_Pervasives_Native.Some (Null) -> begin
     false
     end
| uu___ -> begin
     true
     end))


let rec params_json : Prims.list<query_param>  ->  Prims.list<Capability.jval> = (fun ( ps  :  Prims.list<query_param> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
     ((param_json p))::(params_json t)
     end))


let rec fields_json = (fun ( cc  :  column_codec<'r> ) ( s  :  Prims.list<(Prims.string * column_type)> ) -> (match (s) with
| [] -> begin
     []
     end
| (f)::t -> begin
     ((cc.field_json f))::(fields_json cc t)
     end))


let rec preds_json = (fun ( cc  :  column_codec<'r> ) ( w  :  Prims.list<predicate> ) -> (match (w) with
| [] -> begin
     []
     end
| (p)::t -> begin
     ((predicate_json cc p))::(preds_json cc t)
     end))


let rec keys_json : Prims.list<sort_key>  ->  Prims.list<Capability.jval> = (fun ( o  :  Prims.list<sort_key> ) -> (match (o) with
| [] -> begin
     []
     end
| (k)::t -> begin
     ((sort_key_json k))::(keys_json t)
     end))


let opt_int_json : Prims.string  ->  FStar_Pervasives_Native.option<Prims.int>  ->  Prims.list<(Prims.string * Capability.jval)> = (fun ( name  :  Prims.string ) ( v  :  FStar_Pervasives_Native.option<Prims.int> ) -> (match (v) with
| FStar_Pervasives_Native.Some (n) -> begin
     (((name), (Capability.JInt (n))))::[]
     end
| FStar_Pervasives_Native.None -> begin
     []
     end))


let query_tail = (fun ( cc  :  column_codec<'r> ) ( q  :  query ) -> (ListAlgebra.app (opt_int_json "timeoutMs" q.q_timeout_ms) (ListAlgebra.app (opt_int_json "pageSize" q.q_page_size) (ListAlgebra.app (match (q.q_where) with
| [] -> begin
     []
     end
| uu___ -> begin
     ((("where"), (Capability.JArr ((preds_json cc q.q_where)))))::[]
     end) (match (q.q_order_by) with
| [] -> begin
     []
     end
| uu___ -> begin
     ((("orderBy"), (Capability.JArr ((keys_json q.q_order_by)))))::[]
     end)))))


let query_json = (fun ( cc  :  column_codec<'r> ) ( q  :  query ) -> Capability.JObj (((("$type"), (Capability.JStr ("query"))))::((("id"), (Capability.JStr (q.q_id))))::((("params"), (Capability.JArr ((params_json q.q_params)))))::((("resultSchema"), (Capability.JArr ((fields_json cc q.q_schema)))))::((("effect"), ((effect_json q.q_effect))))::((("source"), ((cc.source_json q.q_source))))::(query_tail cc q)))


let rec where_fault_at : Prims.list<(Prims.string * column_type)>  ->  Prims.nat  ->  Prims.list<predicate>  ->  FStar_Pervasives_Native.option<(Prims.nat * query_error)> = (fun ( s  :  Prims.list<(Prims.string * column_type)> ) ( i  :  Prims.nat ) ( w  :  Prims.list<predicate> ) -> (match (w) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (p)::t -> begin
     (match ((predicate_fault s p)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (((i), (e)))
     end
| FStar_Pervasives_Native.None -> begin
     (where_fault_at s (i + (Prims.parse_int "1")) t)
     end)
     end))


let rec order_fault_at : Prims.list<(Prims.string * column_type)>  ->  Prims.list<Prims.string>  ->  Prims.nat  ->  Prims.list<sort_key>  ->  FStar_Pervasives_Native.option<(Prims.nat * query_error)> = (fun ( s  :  Prims.list<(Prims.string * column_type)> ) ( seen  :  Prims.list<Prims.string> ) ( i  :  Prims.nat ) ( o  :  Prims.list<sort_key> ) -> (match (o) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (k)::t -> begin
      
if (not ((ListAlgebra.mem k.k_column (keys s)))) then begin
     FStar_Pervasives_Native.Some (((i), (UnknownColumn (k.k_column, (keys s)))))
     end else begin
      
if (ListAlgebra.mem k.k_column seen) then begin
     FStar_Pervasives_Native.Some (((i), (DuplicateSortColumn (k.k_column))))
     end else begin
     (order_fault_at s ((k.k_column)::seen) (i + (Prims.parse_int "1")) t)
     end
     end
     end))


let admission_fault_at : query  ->  FStar_Pervasives_Native.option<(Prims.list<Capability.path_seg> * query_error)> = (fun ( q  :  query ) -> (match ((repeated [] (param_names q.q_params))) with
| (d)::uu___ -> begin
     FStar_Pervasives_Native.Some ((((Capability.Key ("params"))::[]), (DuplicateParam (d))))
     end
| [] -> begin
     (match ((where_fault_at q.q_schema (Prims.parse_int "0") q.q_where)) with
| FStar_Pervasives_Native.Some (i, e) -> begin
     FStar_Pervasives_Native.Some ((((Capability.Key ("where"))::(Capability.Index (i))::[]), (e)))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((order_fault_at q.q_schema [] (Prims.parse_int "0") q.q_order_by)) with
| FStar_Pervasives_Native.Some (i, e) -> begin
     FStar_Pervasives_Native.Some ((((Capability.Key ("orderBy"))::(Capability.Index (i))::[]), (e)))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end))


let query_tag_of : Capability.decoder<unit> = (tagged_of (fun ( s  :  Prims.string ) ->  
if (Prims.op_Equals s "query") then begin
     FStar_Pervasives_Native.Some (())
     end else begin
     FStar_Pervasives_Native.None
     end))


let query_of_j = (fun ( cc  :  column_codec<'r> ) ( el  :  Capability.jval ) -> (match ((Capability.field "$type" query_tag_of el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (()) -> begin
     (match ((Capability.field "id" Capability.d_str el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (id) -> begin
     (match ((Capability.field "params" (Capability.d_list param_of_j) el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (ps) -> begin
     (match ((Capability.field "resultSchema" (Capability.d_list (column_read cc.field_of)) el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (sch) -> begin
     (match ((Capability.field "effect" effect_of_j el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (eff) -> begin
     (match ((Capability.opt_field "timeoutMs" Capability.d_int el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (tmo) -> begin
     (match ((Capability.opt_field "pageSize" Capability.d_int el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (pg) -> begin
     (match ((Capability.opt_field "where" (Capability.d_list (predicate_of_j cc)) el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (wh) -> begin
     (match ((Capability.opt_field "orderBy" (Capability.d_list sort_key_of_j) el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (ob) -> begin
     (match ((Capability.field "source" (column_read cc.source_of) el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (src) -> begin
     (

let q = {q_id = id; q_params = ps; q_schema = sch; q_effect = eff; q_source = src; q_timeout_ms = tmo; q_page_size = pg; q_where = (match (wh) with
| FStar_Pervasives_Native.Some (w) -> begin
     w
     end
| FStar_Pervasives_Native.None -> begin
     []
     end); q_order_by = (match (ob) with
| FStar_Pervasives_Native.Some (o) -> begin
     o
     end
| FStar_Pervasives_Native.None -> begin
     []
     end)}
in (match ((admission_fault_at q)) with
| FStar_Pervasives_Native.Some (p, uu___) -> begin
     Capability.Error ({Capability.d_code = Capability.OutOfRange; Capability.d_path = p})
     end
| FStar_Pervasives_Native.None -> begin
     Capability.Ok (q)
     end))
     end)
     end)
     end)
     end)
     end)
     end)
     end)
     end)
     end)
     end))


let rec all_carried : Prims.list<predicate>  ->  Prims.bool = (fun ( w  :  Prims.list<predicate> ) -> (match (w) with
| [] -> begin
     true
     end
| (p)::t -> begin
     ((literal_carried p) && (all_carried t))
     end))

type query_result<'r> = {qr_rows : 'r; qr_page_num : Prims.int; qr_total : FStar_Pervasives_Native.option<Prims.int>; qr_next : FStar_Pervasives_Native.option<Prims.string>}


let __proj__Mkquery_result__item__qr_rows = (fun ( projectee  :  query_result<'r> ) -> (match (projectee) with
| {qr_rows = qr_rows; qr_page_num = qr_page_num; qr_total = qr_total; qr_next = qr_next} -> begin
     qr_rows
     end))


let __proj__Mkquery_result__item__qr_page_num = (fun ( projectee  :  query_result<'r> ) -> (match (projectee) with
| {qr_rows = qr_rows; qr_page_num = qr_page_num; qr_total = qr_total; qr_next = qr_next} -> begin
     qr_page_num
     end))


let __proj__Mkquery_result__item__qr_total = (fun ( projectee  :  query_result<'r> ) -> (match (projectee) with
| {qr_rows = qr_rows; qr_page_num = qr_page_num; qr_total = qr_total; qr_next = qr_next} -> begin
     qr_total
     end))


let __proj__Mkquery_result__item__qr_next = (fun ( projectee  :  query_result<'r> ) -> (match (projectee) with
| {qr_rows = qr_rows; qr_page_num = qr_page_num; qr_total = qr_total; qr_next = qr_next} -> begin
     qr_next
     end))


let result_tail = (fun ( qr  :  query_result<'r> ) -> (ListAlgebra.app (opt_int_json "totalRowCount" qr.qr_total) (match (qr.qr_next) with
| FStar_Pervasives_Native.Some (t) -> begin
     ((("nextPageToken"), (Capability.JStr (t))))::[]
     end
| FStar_Pervasives_Native.None -> begin
     []
     end)))


let result_json = (fun ( cc  :  column_codec<'r> ) ( qr  :  query_result<'r> ) -> Capability.JObj (((("$type"), (Capability.JStr ("queryResult"))))::((("rows"), ((cc.rows_json qr.qr_rows))))::((("pageNum"), (Capability.JInt (qr.qr_page_num))))::(result_tail qr)))


let result_tag_of : Capability.decoder<unit> = (tagged_of (fun ( s  :  Prims.string ) ->  
if (Prims.op_Equals s "queryResult") then begin
     FStar_Pervasives_Native.Some (())
     end else begin
     FStar_Pervasives_Native.None
     end))


let result_of_j = (fun ( cc  :  column_codec<'r> ) ( el  :  Capability.jval ) -> (match ((Capability.field "$type" result_tag_of el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (()) -> begin
     (match ((Capability.field "rows" (column_read cc.rows_of) el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (rows) -> begin
     (match ((Capability.field "pageNum" Capability.d_int el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (pn) -> begin
     (match ((Capability.opt_field "totalRowCount" Capability.d_int el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (trc) -> begin
     (match ((Capability.opt_field "nextPageToken" Capability.d_str el)) with
| Capability.Error (e) -> begin
     Capability.Error (e)
     end
| Capability.Ok (tok) -> begin
     Capability.Ok ({qr_rows = rows; qr_page_num = pn; qr_total = trc; qr_next = tok})
     end)
     end)
     end)
     end)
     end))


let deferred_result_json = (fun ( cc  :  column_codec<'r> ) ( d  :  Capability.deferred<query_result<'r>> ) -> (Capability.deferred_json (result_json cc) d))


let deferred_result_of_j = (fun ( cc  :  column_codec<'r> ) -> (Capability.deferred_dec (result_of_j cc)))

type wire_seg =
| WKey of Prims.string
| WIndex of Prims.int


let uu___is_WKey : wire_seg  ->  Prims.bool = (fun ( projectee  :  wire_seg ) -> (match (projectee) with
| WKey (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__WKey__item___0 : wire_seg  ->  Prims.string = (fun ( projectee  :  wire_seg ) -> (match (projectee) with
| WKey (_0) -> begin
     _0
     end))


let uu___is_WIndex : wire_seg  ->  Prims.bool = (fun ( projectee  :  wire_seg ) -> (match (projectee) with
| WIndex (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__WIndex__item___0 : wire_seg  ->  Prims.int = (fun ( projectee  :  wire_seg ) -> (match (projectee) with
| WIndex (_0) -> begin
     _0
     end))

type wire_decode_error = {we_code : Capability.decode_code; we_path : Prims.list<wire_seg>; we_expected : Prims.string; we_message : Prims.string}


let __proj__Mkwire_decode_error__item__we_code : wire_decode_error  ->  Capability.decode_code = (fun ( projectee  :  wire_decode_error ) -> (match (projectee) with
| {we_code = we_code; we_path = we_path; we_expected = we_expected; we_message = we_message} -> begin
     we_code
     end))


let __proj__Mkwire_decode_error__item__we_path : wire_decode_error  ->  Prims.list<wire_seg> = (fun ( projectee  :  wire_decode_error ) -> (match (projectee) with
| {we_code = we_code; we_path = we_path; we_expected = we_expected; we_message = we_message} -> begin
     we_path
     end))


let __proj__Mkwire_decode_error__item__we_expected : wire_decode_error  ->  Prims.string = (fun ( projectee  :  wire_decode_error ) -> (match (projectee) with
| {we_code = we_code; we_path = we_path; we_expected = we_expected; we_message = we_message} -> begin
     we_expected
     end))


let __proj__Mkwire_decode_error__item__we_message : wire_decode_error  ->  Prims.string = (fun ( projectee  :  wire_decode_error ) -> (match (projectee) with
| {we_code = we_code; we_path = we_path; we_expected = we_expected; we_message = we_message} -> begin
     we_message
     end))

type wire_query_error =
| QRefused of query_error
| QPolicyRefused of Prims.string * Prims.string * Prims.list<Prims.string>
| QApprovalRequired of Prims.string
| QUnreadableArgs of wire_decode_error
| QPredicateNotHonoured of predicate
| QOrderNotHonoured of Prims.string


let uu___is_QRefused : wire_query_error  ->  Prims.bool = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QRefused (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__QRefused__item___0 : wire_query_error  ->  query_error = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QRefused (_0) -> begin
     _0
     end))


let uu___is_QPolicyRefused : wire_query_error  ->  Prims.bool = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QPolicyRefused (policy, reason, allowed) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__QPolicyRefused__item__policy : wire_query_error  ->  Prims.string = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QPolicyRefused (policy, reason, allowed) -> begin
     policy
     end))


let __proj__QPolicyRefused__item__reason : wire_query_error  ->  Prims.string = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QPolicyRefused (policy, reason, allowed) -> begin
     reason
     end))


let __proj__QPolicyRefused__item__allowed : wire_query_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QPolicyRefused (policy, reason, allowed) -> begin
     allowed
     end))


let uu___is_QApprovalRequired : wire_query_error  ->  Prims.bool = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QApprovalRequired (policy) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__QApprovalRequired__item__policy : wire_query_error  ->  Prims.string = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QApprovalRequired (policy) -> begin
     policy
     end))


let uu___is_QUnreadableArgs : wire_query_error  ->  Prims.bool = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QUnreadableArgs (error) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__QUnreadableArgs__item__error : wire_query_error  ->  wire_decode_error = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QUnreadableArgs (error) -> begin
     error
     end))


let uu___is_QPredicateNotHonoured : wire_query_error  ->  Prims.bool = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QPredicateNotHonoured (predicate1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__QPredicateNotHonoured__item__predicate : wire_query_error  ->  predicate = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QPredicateNotHonoured (predicate1) -> begin
     predicate1
     end))


let uu___is_QOrderNotHonoured : wire_query_error  ->  Prims.bool = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QOrderNotHonoured (column) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__QOrderNotHonoured__item__column : wire_query_error  ->  Prims.string = (fun ( projectee  :  wire_query_error ) -> (match (projectee) with
| QOrderNotHonoured (column) -> begin
     column
     end))


let code_name : Capability.decode_code  ->  Prims.string = (fun ( c  :  Capability.decode_code ) -> (match (c) with
| Capability.InvalidJson -> begin
     "InvalidJson"
     end
| Capability.MissingField -> begin
     "MissingField"
     end
| Capability.WrongKind -> begin
     "WrongKind"
     end
| Capability.UnknownTag -> begin
     "UnknownTag"
     end
| Capability.OutOfRange -> begin
     "OutOfRange"
     end
| Capability.UndeclaredMember -> begin
     "UndeclaredMember"
     end
| Capability.LimitExceeded -> begin
     "LimitExceeded"
     end
| Capability.NotAdmitted -> begin
     "NotAdmitted"
     end
| Capability.SchemaFault -> begin
     "SchemaFault"
     end))


let code_of_name : Prims.string  ->  FStar_Pervasives_Native.option<Capability.decode_code> = (fun ( s  :  Prims.string ) ->  
if (Prims.op_Equals s "InvalidJson") then begin
     FStar_Pervasives_Native.Some (Capability.InvalidJson)
     end else begin
      
if (Prims.op_Equals s "MissingField") then begin
     FStar_Pervasives_Native.Some (Capability.MissingField)
     end else begin
      
if (Prims.op_Equals s "WrongKind") then begin
     FStar_Pervasives_Native.Some (Capability.WrongKind)
     end else begin
      
if (Prims.op_Equals s "UnknownTag") then begin
     FStar_Pervasives_Native.Some (Capability.UnknownTag)
     end else begin
      
if (Prims.op_Equals s "OutOfRange") then begin
     FStar_Pervasives_Native.Some (Capability.OutOfRange)
     end else begin
      
if (Prims.op_Equals s "UndeclaredMember") then begin
     FStar_Pervasives_Native.Some (Capability.UndeclaredMember)
     end else begin
      
if (Prims.op_Equals s "LimitExceeded") then begin
     FStar_Pervasives_Native.Some (Capability.LimitExceeded)
     end else begin
      
if (Prims.op_Equals s "NotAdmitted") then begin
     FStar_Pervasives_Native.Some (Capability.NotAdmitted)
     end else begin
      
if (Prims.op_Equals s "SchemaFault") then begin
     FStar_Pervasives_Native.Some (Capability.SchemaFault)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
     end
     end
     end
     end
     end
     end
     end)


let rec path_json : Prims.list<wire_seg>  ->  Prims.list<Capability.jval> = (fun ( p  :  Prims.list<wire_seg> ) -> (match (p) with
| [] -> begin
     []
     end
| (WKey (k))::t -> begin
     (Capability.JStr (k))::(path_json t)
     end
| (WIndex (i))::t -> begin
     (Capability.JInt (i))::(path_json t)
     end))


let rec path_items : Prims.list<Capability.jval>  ->  FStar_Pervasives_Native.option<Prims.list<wire_seg>> = (fun ( xs  :  Prims.list<Capability.jval> ) -> (match (xs) with
| [] -> begin
     FStar_Pervasives_Native.Some ([])
     end
| (Capability.JStr (k))::t -> begin
     (match ((path_items t)) with
| FStar_Pervasives_Native.Some (p) -> begin
     FStar_Pervasives_Native.Some ((WKey (k))::p)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| (Capability.JInt (i))::t -> begin
     (match ((path_items t)) with
| FStar_Pervasives_Native.Some (p) -> begin
     FStar_Pervasives_Native.Some ((WIndex (i))::p)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let path_of_j : Capability.decoder<Prims.list<wire_seg>> = (fun ( el  :  Capability.jval ) -> (match (el) with
| Capability.JArr (xs) -> begin
     (match ((path_items xs)) with
| FStar_Pervasives_Native.Some (p) -> begin
     Capability.Ok (p)
     end
| FStar_Pervasives_Native.None -> begin
     Capability.Error ((Capability.refuse Capability.WrongKind))
     end)
     end
| uu___ -> begin
     Capability.Error ((Capability.refuse Capability.WrongKind))
     end))


let decode_error_json : wire_decode_error  ->  Capability.jval = (fun ( e  :  wire_decode_error ) -> Capability.JObj (((("code"), (Capability.JStr ((code_name e.we_code)))))::((("path"), (Capability.JArr ((path_json e.we_path)))))::((("expected"), (Capability.JStr (e.we_expected))))::((("message"), (Capability.JStr (e.we_message))))::[]))


let decode_error_of_j : Capability.decoder<wire_decode_error> = (Capability.both (Capability.field "code" (tagged_of code_of_name)) (Capability.both (Capability.field "path" path_of_j) (Capability.both (Capability.field "expected" Capability.d_str) (Capability.field "message" Capability.d_str) (fun ( x  :  Prims.string ) ( m  :  Prims.string ) -> ((x), (m)))) (fun ( p  :  Prims.list<wire_seg> ) ( xm  :  (Prims.string * Prims.string) ) -> ((p), (xm)))) (fun ( c  :  Capability.decode_code ) ( pxm  :  (Prims.list<wire_seg> * (Prims.string * Prims.string)) ) -> {we_code = c; we_path = (FStar_Pervasives_Native.fst pxm); we_expected = (FStar_Pervasives_Native.fst (FStar_Pervasives_Native.snd pxm)); we_message = (FStar_Pervasives_Native.snd (FStar_Pervasives_Native.snd pxm))}))


let strs_j : Prims.list<Prims.string>  ->  Capability.jval = (fun ( xs  :  Prims.list<Prims.string> ) -> Capability.JArr ((Capability.strs xs)))


let query_error_json = (fun ( cc  :  column_codec<'r> ) ( e  :  wire_query_error ) -> (match (e) with
| QRefused (NoSuchQuery (id, known)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("noSuchQuery"))))::((("id"), (Capability.JStr (id))))::((("known"), ((strs_j known))))::[])
     end
| QRefused (DuplicateQuery (id)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("duplicateQuery"))))::((("id"), (Capability.JStr (id))))::[])
     end
| QRefused (UnknownParam (name, declared)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("unknownParam"))))::((("name"), (Capability.JStr (name))))::((("declared"), ((strs_j declared))))::[])
     end
| QRefused (ParamTypeMismatch (name, expected, got)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("paramTypeMismatch"))))::((("name"), (Capability.JStr (name))))::((("expected"), (Capability.JStr ((column_tag expected)))))::((("got"), (Capability.JStr ((column_tag got)))))::[])
     end
| QRefused (RequiredParamsUnbound (names)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("requiredParamsUnbound"))))::((("names"), ((strs_j names))))::[])
     end
| QRefused (SourceNotResolved (s)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("sourceNotResolved"))))::((("ref"), (Capability.JStr (s))))::[])
     end
| QRefused (ExecutionFailed (detail, recoverable)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("executionFailed"))))::((("detail"), (Capability.JStr (detail))))::((("recoverable"), ((strs_j recoverable))))::[])
     end
| QRefused (Timeout) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("timeout"))))::[])
     end
| QRefused (RequiredParamsNull (names)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("requiredParamsNull"))))::((("names"), ((strs_j names))))::[])
     end
| QRefused (DuplicateParam (name)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("duplicateParam"))))::((("name"), (Capability.JStr (name))))::[])
     end
| QPolicyRefused (policy, reason, allowed) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("policyRefused"))))::((("policy"), (Capability.JStr (policy))))::((("reason"), (Capability.JStr (reason))))::((("allowed"), ((strs_j allowed))))::[])
     end
| QApprovalRequired (policy) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("approvalRequired"))))::((("policy"), (Capability.JStr (policy))))::[])
     end
| QUnreadableArgs (err) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("unreadableArgs"))))::((("error"), ((decode_error_json err))))::[])
     end
| QRefused (UnknownColumn (name, declared)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("unknownColumn"))))::((("name"), (Capability.JStr (name))))::((("declared"), ((strs_j declared))))::[])
     end
| QRefused (PredicateTypeMismatch (column, expected, got)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("predicateTypeMismatch"))))::((("column"), (Capability.JStr (column))))::((("expected"), (Capability.JStr ((column_tag expected)))))::((("got"), (Capability.JStr ((column_tag got)))))::[])
     end
| QRefused (PredicateNotApplicable (predicate1, column, ty)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("predicateNotApplicable"))))::((("predicate"), (Capability.JStr (predicate1))))::((("column"), (Capability.JStr (column))))::((("columnType"), (Capability.JStr ((column_tag ty)))))::[])
     end
| QRefused (IllFormedLiteral (column, reason)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("illFormedLiteral"))))::((("column"), (Capability.JStr (column))))::((("reason"), (Capability.JStr (reason))))::[])
     end
| QRefused (DuplicateSortColumn (column)) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("duplicateSortColumn"))))::((("column"), (Capability.JStr (column))))::[])
     end
| QPredicateNotHonoured (p) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("predicateNotHonoured"))))::((("predicate"), ((predicate_json cc p))))::[])
     end
| QOrderNotHonoured (column) -> begin
     Capability.JObj (((("$type"), (Capability.JStr ("orderNotHonoured"))))::((("column"), (Capability.JStr (column))))::[])
     end))


let s_f : Prims.string  ->  Capability.decoder<Prims.string> = (fun ( name  :  Prims.string ) -> (Capability.field name Capability.d_str))


let sl_f : Prims.string  ->  Capability.decoder<Prims.list<Prims.string>> = (fun ( name  :  Prims.string ) -> (Capability.field name (Capability.d_list Capability.d_str)))


let ty_f : Prims.string  ->  Capability.decoder<column_type> = (fun ( name  :  Prims.string ) -> (Capability.field name col_type_of_j))


let predicate_tag_of : Prims.string  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( s  :  Prims.string ) ->  
if ((((((((Prims.op_Equals s "equalTo") || (Prims.op_Equals s "greaterThan")) || (Prims.op_Equals s "atLeast")) || (Prims.op_Equals s "lessThan")) || (Prims.op_Equals s "atMost")) || (Prims.op_Equals s "contains")) || (Prims.op_Equals s "isNull")) || (Prims.op_Equals s "isNotNull")) then begin
     FStar_Pervasives_Native.Some (s)
     end else begin
     FStar_Pervasives_Native.None
     end)


let qrefused = (fun ( f  :  'a  ->  query_error ) ( x  :  'a ) -> QRefused ((f x)))


let query_error_cases = (fun ( cc  :  column_codec<'r> ) -> ((("noSuchQuery"), ((Capability.both (s_f "id") (sl_f "known") (fun ( id  :  Prims.string ) ( known  :  Prims.list<Prims.string> ) -> QRefused (NoSuchQuery (id, known)))))))::((("duplicateQuery"), ((Capability.d_map (qrefused (fun ( uu___  :  Prims.string ) -> DuplicateQuery (uu___))) (s_f "id")))))::((("unknownParam"), ((Capability.both (s_f "name") (sl_f "declared") (fun ( n  :  Prims.string ) ( d  :  Prims.list<Prims.string> ) -> QRefused (UnknownParam (n, d)))))))::((("paramTypeMismatch"), ((Capability.both (s_f "name") (Capability.both (ty_f "expected") (ty_f "got") (fun ( x  :  column_type ) ( g  :  column_type ) -> ((x), (g)))) (fun ( n  :  Prims.string ) ( xg  :  (column_type * column_type) ) -> QRefused (ParamTypeMismatch (n, (FStar_Pervasives_Native.fst xg), (FStar_Pervasives_Native.snd xg))))))))::((("requiredParamsUnbound"), ((Capability.d_map (qrefused (fun ( uu___  :  Prims.list<Prims.string> ) -> RequiredParamsUnbound (uu___))) (sl_f "names")))))::((("sourceNotResolved"), ((Capability.d_map (qrefused (fun ( uu___  :  Prims.string ) -> SourceNotResolved (uu___))) (s_f "ref")))))::((("executionFailed"), ((Capability.both (s_f "detail") (sl_f "recoverable") (fun ( d  :  Prims.string ) ( rc  :  Prims.list<Prims.string> ) -> QRefused (ExecutionFailed (d, rc)))))))::((("timeout"), ((Capability.d_succeed (QRefused (Timeout))))))::((("requiredParamsNull"), ((Capability.d_map (qrefused (fun ( uu___  :  Prims.list<Prims.string> ) -> RequiredParamsNull (uu___))) (sl_f "names")))))::((("duplicateParam"), ((Capability.d_map (qrefused (fun ( uu___  :  Prims.string ) -> DuplicateParam (uu___))) (s_f "name")))))::((("policyRefused"), ((Capability.both (s_f "policy") (Capability.both (s_f "reason") (sl_f "allowed") (fun ( rs  :  Prims.string ) ( a  :  Prims.list<Prims.string> ) -> ((rs), (a)))) (fun ( p  :  Prims.string ) ( ra  :  (Prims.string * Prims.list<Prims.string>) ) -> QPolicyRefused (p, (FStar_Pervasives_Native.fst ra), (FStar_Pervasives_Native.snd ra)))))))::((("approvalRequired"), ((Capability.d_map (fun ( uu___  :  Prims.string ) -> QApprovalRequired (uu___)) (s_f "policy")))))::((("unreadableArgs"), ((Capability.d_map (fun ( uu___  :  wire_decode_error ) -> QUnreadableArgs (uu___)) (Capability.field "error" decode_error_of_j)))))::((("unknownColumn"), ((Capability.both (s_f "name") (sl_f "declared") (fun ( n  :  Prims.string ) ( d  :  Prims.list<Prims.string> ) -> QRefused (UnknownColumn (n, d)))))))::((("predicateTypeMismatch"), ((Capability.both (s_f "column") (Capability.both (ty_f "expected") (ty_f "got") (fun ( x  :  column_type ) ( g  :  column_type ) -> ((x), (g)))) (fun ( c  :  Prims.string ) ( xg  :  (column_type * column_type) ) -> QRefused (PredicateTypeMismatch (c, (FStar_Pervasives_Native.fst xg), (FStar_Pervasives_Native.snd xg))))))))::((("predicateNotApplicable"), ((Capability.both (Capability.field "predicate" (tagged_of predicate_tag_of)) (Capability.both (s_f "column") (ty_f "columnType") (fun ( c  :  Prims.string ) ( t  :  column_type ) -> ((c), (t)))) (fun ( p  :  Prims.string ) ( ct  :  (Prims.string * column_type) ) -> QRefused (PredicateNotApplicable (p, (FStar_Pervasives_Native.fst ct), (FStar_Pervasives_Native.snd ct))))))))::((("illFormedLiteral"), ((Capability.both (s_f "column") (s_f "reason") (fun ( c  :  Prims.string ) ( rs  :  Prims.string ) -> QRefused (IllFormedLiteral (c, rs)))))))::((("duplicateSortColumn"), ((Capability.d_map (qrefused (fun ( uu___  :  Prims.string ) -> DuplicateSortColumn (uu___))) (s_f "column")))))::((("predicateNotHonoured"), ((Capability.d_map (fun ( uu___  :  predicate ) -> QPredicateNotHonoured (uu___)) (Capability.field "predicate" (predicate_of_j cc))))))::((("orderNotHonoured"), ((Capability.d_map (fun ( uu___  :  Prims.string ) -> QOrderNotHonoured (uu___)) (s_f "column")))))::[])


let query_error_of_j = (fun ( cc  :  column_codec<'r> ) -> (Capability.tag_dispatch (query_error_cases cc)))


let query_error_carried : wire_query_error  ->  Prims.bool = (fun ( e  :  wire_query_error ) -> (match (e) with
| QRefused (PredicateNotApplicable (p, uu___, uu___1)) -> begin
     (match ((predicate_tag_of p)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___2 -> begin
     false
     end)
     end
| QPredicateNotHonoured (p) -> begin
     (literal_carried p)
     end
| uu___ -> begin
     true
     end))




