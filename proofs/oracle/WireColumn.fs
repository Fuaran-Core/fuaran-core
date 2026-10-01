module WireColumn
type column_type =
| IntType
| FloatType
| BoolType
| StringType
| DateType
| TimestampType
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
| TimestampType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DecimalType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| DecimalType -> begin
     true
     end
| uu___ -> begin
     false
     end))

type cell<'num, 'flt> =
| Int of 'num
| Float of 'flt
| Bool of Prims.bool
| Str of Prims.list<WireCanon.ch>
| Date of Prims.list<WireCanon.ch>
| Timestamp of Prims.list<WireCanon.ch>
| Null
| Decimal of Prims.list<WireCanon.ch>


let uu___is_Int = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Int (i) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Int__item__i = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Int (i) -> begin
     i
     end))


let uu___is_Float = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Float (f) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Float__item__f = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Float (f) -> begin
     f
     end))


let uu___is_Bool = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Bool (b) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Bool__item__b = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Bool (b) -> begin
     b
     end))


let uu___is_Str = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Str (s) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Str__item__s = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Str (s) -> begin
     s
     end))


let uu___is_Date = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Date (s) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Date__item__s = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Date (s) -> begin
     s
     end))


let uu___is_Timestamp = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Timestamp (s) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Timestamp__item__s = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Timestamp (s) -> begin
     s
     end))


let uu___is_Null = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Null -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Decimal = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Decimal (s) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Decimal__item__s = (fun ( projectee  :  cell<'num, 'flt> ) -> (match (projectee) with
| Decimal (s) -> begin
     s
     end))

type column<'num, 'flt> = {name : Prims.list<WireCanon.ch>; ctype : column_type; cells : Prims.list<cell<'num, 'flt>>}


let __proj__Mkcolumn__item__name = (fun ( projectee  :  column<'num, 'flt> ) -> (match (projectee) with
| {name = name; ctype = ctype; cells = cells} -> begin
     name
     end))


let __proj__Mkcolumn__item__ctype = (fun ( projectee  :  column<'num, 'flt> ) -> (match (projectee) with
| {name = name; ctype = ctype; cells = cells} -> begin
     ctype
     end))


let __proj__Mkcolumn__item__cells = (fun ( projectee  :  column<'num, 'flt> ) -> (match (projectee) with
| {name = name; ctype = ctype; cells = cells} -> begin
     cells
     end))

type table<'num, 'flt> = {schema : Prims.list<(Prims.list<WireCanon.ch> * column_type)>; columns : Prims.list<column<'num, 'flt>>}


let __proj__Mktable__item__schema = (fun ( projectee  :  table<'num, 'flt> ) -> (match (projectee) with
| {schema = schema; columns = columns} -> begin
     schema
     end))


let __proj__Mktable__item__columns = (fun ( projectee  :  table<'num, 'flt> ) -> (match (projectee) with
| {schema = schema; columns = columns} -> begin
     columns
     end))

type data_source<'num, 'flt> =
| Embedded of table<'num, 'flt>
| Ref of Prims.list<WireCanon.ch>


let uu___is_Embedded = (fun ( projectee  :  data_source<'num, 'flt> ) -> (match (projectee) with
| Embedded (t) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Embedded__item__t = (fun ( projectee  :  data_source<'num, 'flt> ) -> (match (projectee) with
| Embedded (t) -> begin
     t
     end))


let uu___is_Ref = (fun ( projectee  :  data_source<'num, 'flt> ) -> (match (projectee) with
| Ref (r) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ref__item__r = (fun ( projectee  :  data_source<'num, 'flt> ) -> (match (projectee) with
| Ref (r) -> begin
     r
     end))

type table_fault =
| DuplicateSchemaName of Prims.list<WireCanon.ch>
| DuplicateColumnName of Prims.list<WireCanon.ch>
| SchemaNameWithoutColumn
| ColumnOutsideSchema
| DuplicateColumnKey of Prims.list<WireCanon.ch>


let uu___is_DuplicateSchemaName : table_fault  ->  Prims.bool = (fun ( projectee  :  table_fault ) -> (match (projectee) with
| DuplicateSchemaName (n) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateSchemaName__item__n : table_fault  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  table_fault ) -> (match (projectee) with
| DuplicateSchemaName (n) -> begin
     n
     end))


let uu___is_DuplicateColumnName : table_fault  ->  Prims.bool = (fun ( projectee  :  table_fault ) -> (match (projectee) with
| DuplicateColumnName (n) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateColumnName__item__n : table_fault  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  table_fault ) -> (match (projectee) with
| DuplicateColumnName (n) -> begin
     n
     end))


let uu___is_SchemaNameWithoutColumn : table_fault  ->  Prims.bool = (fun ( projectee  :  table_fault ) -> (match (projectee) with
| SchemaNameWithoutColumn -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ColumnOutsideSchema : table_fault  ->  Prims.bool = (fun ( projectee  :  table_fault ) -> (match (projectee) with
| ColumnOutsideSchema -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DuplicateColumnKey : table_fault  ->  Prims.bool = (fun ( projectee  :  table_fault ) -> (match (projectee) with
| DuplicateColumnKey (k) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateColumnKey__item__k : table_fault  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  table_fault ) -> (match (projectee) with
| DuplicateColumnKey (k) -> begin
     k
     end))

type column_error =
| MissingField of Prims.list<WireCanon.ch>
| MissingColumn of Prims.list<WireCanon.ch>
| MalformedShape
| UnknownType of Prims.list<WireCanon.ch>
| TypeMismatch of Prims.list<WireCanon.ch> * column_type
| LengthMismatch of Prims.list<WireCanon.ch>
| NonFiniteFloat of Prims.list<WireCanon.ch>
| Malformed of table_fault
| RaggedColumns of Prims.list<WireCanon.ch>
| OutOfModel


let uu___is_MissingField : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| MissingField (field) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MissingField__item__field : column_error  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  column_error ) -> (match (projectee) with
| MissingField (field) -> begin
     field
     end))


let uu___is_MissingColumn : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| MissingColumn (col) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MissingColumn__item__col : column_error  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  column_error ) -> (match (projectee) with
| MissingColumn (col) -> begin
     col
     end))


let uu___is_MalformedShape : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| MalformedShape -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UnknownType : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| UnknownType (got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownType__item__got : column_error  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  column_error ) -> (match (projectee) with
| UnknownType (got) -> begin
     got
     end))


let uu___is_TypeMismatch : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| TypeMismatch (col, expected) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TypeMismatch__item__col : column_error  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  column_error ) -> (match (projectee) with
| TypeMismatch (col, expected) -> begin
     col
     end))


let __proj__TypeMismatch__item__expected : column_error  ->  column_type = (fun ( projectee  :  column_error ) -> (match (projectee) with
| TypeMismatch (col, expected) -> begin
     expected
     end))


let uu___is_LengthMismatch : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| LengthMismatch (col) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LengthMismatch__item__col : column_error  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  column_error ) -> (match (projectee) with
| LengthMismatch (col) -> begin
     col
     end))


let uu___is_NonFiniteFloat : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| NonFiniteFloat (col) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NonFiniteFloat__item__col : column_error  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  column_error ) -> (match (projectee) with
| NonFiniteFloat (col) -> begin
     col
     end))


let uu___is_Malformed : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| Malformed (fault) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Malformed__item__fault : column_error  ->  table_fault = (fun ( projectee  :  column_error ) -> (match (projectee) with
| Malformed (fault) -> begin
     fault
     end))


let uu___is_RaggedColumns : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| RaggedColumns (col) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RaggedColumns__item__col : column_error  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  column_error ) -> (match (projectee) with
| RaggedColumns (col) -> begin
     col
     end))


let uu___is_OutOfModel : column_error  ->  Prims.bool = (fun ( projectee  :  column_error ) -> (match (projectee) with
| OutOfModel -> begin
     true
     end
| uu___ -> begin
     false
     end))

type res<'a> =
| Good of 'a
| Bad of column_error


let uu___is_Good = (fun ( projectee  :  res<'a> ) -> (match (projectee) with
| Good (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Good__item__v = (fun ( projectee  :  res<'a> ) -> (match (projectee) with
| Good (v) -> begin
     v
     end))


let uu___is_Bad = (fun ( projectee  :  res<'a> ) -> (match (projectee) with
| Bad (e) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Bad__item__e = (fun ( projectee  :  res<'a> ) -> (match (projectee) with
| Bad (e) -> begin
     e
     end))

type host<'num, 'flt> = {to_float : 'num  ->  'flt; int_text : 'num  ->  Prims.list<WireCanon.ch>; finite : 'flt  ->  Prims.bool; zero_int : 'num; zero_float : 'flt; is_date : Prims.list<WireCanon.ch>  ->  Prims.bool; is_timestamp : Prims.list<WireCanon.ch>  ->  Prims.bool}


let __proj__Mkhost__item__to_float = (fun ( projectee  :  host<'num, 'flt> ) -> (match (projectee) with
| {to_float = to_float; int_text = int_text; finite = finite; zero_int = zero_int; zero_float = zero_float; is_date = is_date; is_timestamp = is_timestamp} -> begin
     to_float
     end))


let __proj__Mkhost__item__int_text = (fun ( projectee  :  host<'num, 'flt> ) -> (match (projectee) with
| {to_float = to_float; int_text = int_text; finite = finite; zero_int = zero_int; zero_float = zero_float; is_date = is_date; is_timestamp = is_timestamp} -> begin
     int_text
     end))


let __proj__Mkhost__item__finite = (fun ( projectee  :  host<'num, 'flt> ) -> (match (projectee) with
| {to_float = to_float; int_text = int_text; finite = finite; zero_int = zero_int; zero_float = zero_float; is_date = is_date; is_timestamp = is_timestamp} -> begin
     finite
     end))


let __proj__Mkhost__item__zero_int = (fun ( projectee  :  host<'num, 'flt> ) -> (match (projectee) with
| {to_float = to_float; int_text = int_text; finite = finite; zero_int = zero_int; zero_float = zero_float; is_date = is_date; is_timestamp = is_timestamp} -> begin
     zero_int
     end))


let __proj__Mkhost__item__zero_float = (fun ( projectee  :  host<'num, 'flt> ) -> (match (projectee) with
| {to_float = to_float; int_text = int_text; finite = finite; zero_int = zero_int; zero_float = zero_float; is_date = is_date; is_timestamp = is_timestamp} -> begin
     zero_float
     end))


let __proj__Mkhost__item__is_date = (fun ( projectee  :  host<'num, 'flt> ) -> (match (projectee) with
| {to_float = to_float; int_text = int_text; finite = finite; zero_int = zero_int; zero_float = zero_float; is_date = is_date; is_timestamp = is_timestamp} -> begin
     is_date
     end))


let __proj__Mkhost__item__is_timestamp = (fun ( projectee  :  host<'num, 'flt> ) -> (match (projectee) with
| {to_float = to_float; int_text = int_text; finite = finite; zero_int = zero_int; zero_float = zero_float; is_date = is_date; is_timestamp = is_timestamp} -> begin
     is_timestamp
     end))


let schema_key : Prims.list<WireCanon.ch> = (WireCanon.CPlain ("s"))::(WireCanon.CHexCh (WireCanon.HDc))::(WireCanon.CPlain ("h"))::(WireCanon.CHexCh (WireCanon.HDe))::(WireCanon.CPlain ("m"))::(WireCanon.CHexCh (WireCanon.HDa))::[]


let columns_key : Prims.list<WireCanon.ch> = (WireCanon.CHexCh (WireCanon.HDc))::(WireCanon.CPlain ("o"))::(WireCanon.CPlain ("l"))::(WireCanon.CLu)::(WireCanon.CPlain ("m"))::(WireCanon.CPlain ("n"))::(WireCanon.CPlain ("s"))::[]


let ref_key : Prims.list<WireCanon.ch> = (WireCanon.CPlain ("r"))::(WireCanon.CHexCh (WireCanon.HDe))::(WireCanon.CHexCh (WireCanon.HDf))::[]


let name_key : Prims.list<WireCanon.ch> = (WireCanon.CPlain ("n"))::(WireCanon.CHexCh (WireCanon.HDa))::(WireCanon.CPlain ("m"))::(WireCanon.CHexCh (WireCanon.HDe))::[]


let type_key : Prims.list<WireCanon.ch> = (WireCanon.CPlain ("t"))::(WireCanon.CPlain ("y"))::(WireCanon.CPlain ("p"))::(WireCanon.CHexCh (WireCanon.HDe))::[]


let values_key : Prims.list<WireCanon.ch> = (WireCanon.CPlain ("v"))::(WireCanon.CHexCh (WireCanon.HDa))::(WireCanon.CPlain ("l"))::(WireCanon.CLu)::(WireCanon.CHexCh (WireCanon.HDe))::(WireCanon.CPlain ("s"))::[]


let validity_key : Prims.list<WireCanon.ch> = (WireCanon.CPlain ("v"))::(WireCanon.CHexCh (WireCanon.HDa))::(WireCanon.CPlain ("l"))::(WireCanon.CPlain ("i"))::(WireCanon.CHexCh (WireCanon.HDd))::(WireCanon.CPlain ("i"))::(WireCanon.CPlain ("t"))::(WireCanon.CPlain ("y"))::[]


let tag : column_type  ->  Prims.list<WireCanon.ch> = (fun ( t  :  column_type ) -> (match (t) with
| IntType -> begin
     (WireCanon.CPlain ("i"))::(WireCanon.CPlain ("n"))::(WireCanon.CPlain ("t"))::[]
     end
| FloatType -> begin
     (WireCanon.CHexCh (WireCanon.HDf))::(WireCanon.CPlain ("l"))::(WireCanon.CPlain ("o"))::(WireCanon.CHexCh (WireCanon.HDa))::(WireCanon.CPlain ("t"))::[]
     end
| BoolType -> begin
     (WireCanon.CHexCh (WireCanon.HDb))::(WireCanon.CPlain ("o"))::(WireCanon.CPlain ("o"))::(WireCanon.CPlain ("l"))::[]
     end
| StringType -> begin
     (WireCanon.CPlain ("s"))::(WireCanon.CPlain ("t"))::(WireCanon.CPlain ("r"))::(WireCanon.CPlain ("i"))::(WireCanon.CPlain ("n"))::(WireCanon.CPlain ("g"))::[]
     end
| DateType -> begin
     (WireCanon.CHexCh (WireCanon.HDd))::(WireCanon.CHexCh (WireCanon.HDa))::(WireCanon.CPlain ("t"))::(WireCanon.CHexCh (WireCanon.HDe))::[]
     end
| TimestampType -> begin
     (WireCanon.CPlain ("t"))::(WireCanon.CPlain ("i"))::(WireCanon.CPlain ("m"))::(WireCanon.CHexCh (WireCanon.HDe))::(WireCanon.CPlain ("s"))::(WireCanon.CPlain ("t"))::(WireCanon.CHexCh (WireCanon.HDa))::(WireCanon.CPlain ("m"))::(WireCanon.CPlain ("p"))::[]
     end
| DecimalType -> begin
     (WireCanon.CHexCh (WireCanon.HDd))::(WireCanon.CHexCh (WireCanon.HDe))::(WireCanon.CHexCh (WireCanon.HDc))::(WireCanon.CPlain ("i"))::(WireCanon.CPlain ("m"))::(WireCanon.CHexCh (WireCanon.HDa))::(WireCanon.CPlain ("l"))::[]
     end))


let all_types : Prims.list<column_type> = (IntType)::(FloatType)::(BoolType)::(StringType)::(DateType)::(TimestampType)::(DecimalType)::[]


let rec find_tag : Prims.list<WireCanon.ch>  ->  Prims.list<column_type>  ->  FStar_Pervasives_Native.option<column_type> = (fun ( s  :  Prims.list<WireCanon.ch> ) ( ts  :  Prims.list<column_type> ) -> (match (ts) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (t)::rest -> begin
      
if (Prims.op_Equals (tag t) s) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     (find_tag s rest)
     end
     end))


let of_tag : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<column_type> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (find_tag s all_types))


let widens : column_type  ->  column_type  ->  Prims.bool = (fun ( from  :  column_type ) ( target  :  column_type ) -> (((Prims.op_Equals from target) || ((Prims.op_Equals from IntType) && (Prims.op_Equals target FloatType))) || ((Prims.op_Equals from IntType) && (Prims.op_Equals target DecimalType))))


let type_of = (fun ( c  :  cell<'num, 'flt> ) -> (match (c) with
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
| Decimal (uu___) -> begin
     FStar_Pervasives_Native.Some (DecimalType)
     end
| Null -> begin
     FStar_Pervasives_Native.None
     end))


let zero_ch : WireCanon.ch = WireCanon.CHexCh (WireCanon.HD0)


let is_digit : WireCanon.ch  ->  Prims.bool = (fun ( c  :  WireCanon.ch ) -> (match (c) with
| WireCanon.CHexCh (d) -> begin
     (WireCanon.is_dec d)
     end
| uu___ -> begin
     false
     end))


let rec all_digits : Prims.list<WireCanon.ch>  ->  Prims.bool = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((is_digit c) && (all_digits t))
     end))


let is_digits : Prims.list<WireCanon.ch>  ->  Prims.bool = (fun ( s  :  Prims.list<WireCanon.ch> ) -> ((match (s) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_digits s)))


let rec split_dot : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<(Prims.list<WireCanon.ch> * Prims.list<WireCanon.ch>)> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if (Prims.op_Equals c WireCanon.CDot) then begin
     FStar_Pervasives_Native.Some ((([]), (t)))
     end else begin
     (match ((split_dot t)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (before, after) -> begin
     FStar_Pervasives_Native.Some ((((c)::before), (after)))
     end)
     end
     end))


let rec trim_start : Prims.list<WireCanon.ch>  ->  Prims.list<WireCanon.ch> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     []
     end
| (c)::t -> begin
      
if (Prims.op_Equals c zero_ch) then begin
     (trim_start t)
     end else begin
     s
     end
     end))


let rec trim_end : Prims.list<WireCanon.ch>  ->  Prims.list<WireCanon.ch> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (

let r = (trim_end t)
in  
if ((Prims.op_Equals c zero_ch) && (match (r) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     []
     end else begin
     (c)::r
     end)
     end))

type dparts = {negative : Prims.bool; ip : Prims.list<WireCanon.ch>; fp : Prims.list<WireCanon.ch>}


let __proj__Mkdparts__item__negative : dparts  ->  Prims.bool = (fun ( projectee  :  dparts ) -> (match (projectee) with
| {negative = negative; ip = ip; fp = fp} -> begin
     negative
     end))


let __proj__Mkdparts__item__ip : dparts  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  dparts ) -> (match (projectee) with
| {negative = negative; ip = ip; fp = fp} -> begin
     ip
     end))


let __proj__Mkdparts__item__fp : dparts  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  dparts ) -> (match (projectee) with
| {negative = negative; ip = ip; fp = fp} -> begin
     fp
     end))


let parts : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<dparts> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c0)::rest -> begin
     (

let neg = (Prims.op_Equals c0 WireCanon.CMinus)
in (

let body =  
if neg then begin
     rest
     end else begin
     s
     end
in (match ((split_dot body)) with
| FStar_Pervasives_Native.None -> begin
      
if (not ((is_digits body))) then begin
     FStar_Pervasives_Native.None
     end else begin
     (

let ip' = (trim_start body)
in FStar_Pervasives_Native.Some ({negative = (neg && (match (ip') with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end)); ip = ip'; fp = []}))
     end
     end
| FStar_Pervasives_Native.Some (ip0, fp0) -> begin
      
if (not (((is_digits ip0) && (is_digits fp0)))) then begin
     FStar_Pervasives_Native.None
     end else begin
     (

let ip' = (trim_start ip0)
in (

let fp' = (trim_end fp0)
in (

let is_zero = ((match (ip') with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) && (match (fp') with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end))
in FStar_Pervasives_Native.Some ({negative = (neg && (not (is_zero))); ip = ip'; fp = fp'}))))
     end
     end)))
     end))


let render_parts : dparts  ->  Prims.list<WireCanon.ch> = (fun ( p  :  dparts ) -> (WireCanon.app ( 
if p.negative then begin
     (WireCanon.CMinus)::[]
     end else begin
     []
     end) (WireCanon.app ( 
if (match (p.ip) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     (zero_ch)::[]
     end else begin
     p.ip
     end) ( 
if (match (p.fp) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     []
     end else begin
     (WireCanon.CDot)::p.fp
     end))))


let try_canonical : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<Prims.list<WireCanon.ch>> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match ((parts s)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (p) -> begin
     FStar_Pervasives_Native.Some ((render_parts p))
     end))


let is_canonical : Prims.list<WireCanon.ch>  ->  Prims.bool = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (Prims.op_Equals (try_canonical s) (FStar_Pervasives_Native.Some (s))))


let dec_zero : Prims.list<WireCanon.ch> = (zero_ch)::[]


let rec schema_names : Prims.list<(Prims.list<WireCanon.ch> * column_type)>  ->  Prims.list<Prims.list<WireCanon.ch>> = (fun ( s  :  Prims.list<(Prims.list<WireCanon.ch> * column_type)> ) -> (match (s) with
| [] -> begin
     []
     end
| ((n, uu___))::t -> begin
     (n)::(schema_names t)
     end))


let rec column_names = (fun ( cs  :  Prims.list<column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (c.name)::(column_names t)
     end))


let rec first_dup_go : Prims.list<Prims.list<WireCanon.ch>>  ->  Prims.list<Prims.list<WireCanon.ch>>  ->  FStar_Pervasives_Native.option<Prims.list<WireCanon.ch>> = (fun ( seen  :  Prims.list<Prims.list<WireCanon.ch>> ) ( names  :  Prims.list<Prims.list<WireCanon.ch>> ) -> (match (names) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (n)::rest -> begin
      
if (WireCanon.mem n seen) then begin
     FStar_Pervasives_Native.Some (n)
     end else begin
     (first_dup_go ((n)::seen) rest)
     end
     end))


let first_duplicate : Prims.list<Prims.list<WireCanon.ch>>  ->  FStar_Pervasives_Native.option<Prims.list<WireCanon.ch>> = (fun ( names  :  Prims.list<Prims.list<WireCanon.ch>> ) -> (first_dup_go [] names))


let rec not_in : Prims.list<Prims.list<WireCanon.ch>>  ->  Prims.list<Prims.list<WireCanon.ch>>  ->  Prims.list<Prims.list<WireCanon.ch>> = (fun ( names  :  Prims.list<Prims.list<WireCanon.ch>> ) ( against  :  Prims.list<Prims.list<WireCanon.ch>> ) -> (match (names) with
| [] -> begin
     []
     end
| (n)::rest -> begin
      
if (WireCanon.mem n against) then begin
     (not_in rest against)
     end else begin
     (n)::(not_in rest against)
     end
     end))


let rec find_column = (fun ( n  :  Prims.list<WireCanon.ch> ) ( cs  :  Prims.list<column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if (Prims.op_Equals c.name n) then begin
     FStar_Pervasives_Native.Some (c)
     end else begin
     (find_column n t)
     end
     end))


let rec type_fault = (fun ( s  :  Prims.list<(Prims.list<WireCanon.ch> * column_type)> ) ( cs  :  Prims.list<column<'num, 'flt>> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, ty))::rest -> begin
     (match ((find_column n cs)) with
| FStar_Pervasives_Native.Some (c) -> begin
      
if (Prims.op_Less_Greater c.ctype ty) then begin
     FStar_Pervasives_Native.Some (TypeMismatch (n, ty))
     end else begin
     (type_fault rest cs)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (type_fault rest cs)
     end)
     end))


let rec same_len = (fun ( xs  :  Prims.list<'a> ) ( ys  :  Prims.list<'b> ) -> (match (((xs), (ys))) with
| ([], []) -> begin
     true
     end
| ((uu___)::xt, (uu___1)::yt) -> begin
     (same_len xt yt)
     end
| uu___ -> begin
     false
     end))


let rec first_ragged = (fun ( cells0  :  Prims.list<cell<'num, 'flt>> ) ( rest  :  Prims.list<column<'num, 'flt>> ) -> (match (rest) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if (same_len cells0 c.cells) then begin
     (first_ragged cells0 t)
     end else begin
     FStar_Pervasives_Native.Some (RaggedColumns (c.name))
     end
     end))


let ragged = (fun ( cs  :  Prims.list<column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (first)::rest -> begin
     (first_ragged first.cells rest)
     end))


let cell_fault = (fun ( h  :  host<'num, 'flt> ) ( cname  :  Prims.list<WireCanon.ch> ) ( ty  :  column_type ) ( c  :  cell<'num, 'flt> ) -> (match (c) with
| Null -> begin
     FStar_Pervasives_Native.None
     end
| uu___ -> begin
     (

let outside = (match ((type_of c)) with
| FStar_Pervasives_Native.Some (t) -> begin
     (not ((widens t ty)))
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)
in  
if outside then begin
     FStar_Pervasives_Native.Some (TypeMismatch (cname, ty))
     end else begin
     (match (c) with
| Float (f) -> begin
      
if (h.finite f) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (NonFiniteFloat (cname))
     end
     end
| Decimal (s) -> begin
      
if (is_canonical s) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (MalformedShape)
     end
     end
| Date (s) -> begin
      
if (h.is_date s) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (MalformedShape)
     end
     end
| Timestamp (s) -> begin
      
if (h.is_timestamp s) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (MalformedShape)
     end
     end
| uu___1 -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end))


let rec first_uncarriable = (fun ( h  :  host<'num, 'flt> ) ( cname  :  Prims.list<WireCanon.ch> ) ( ty  :  column_type ) ( cs  :  Prims.list<cell<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
     (match ((cell_fault h cname ty c)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (e)
     end
| FStar_Pervasives_Native.None -> begin
     (first_uncarriable h cname ty t)
     end)
     end))


let rec cells_fault = (fun ( h  :  host<'num, 'flt> ) ( s  :  Prims.list<(Prims.list<WireCanon.ch> * column_type)> ) ( cs  :  Prims.list<column<'num, 'flt>> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, uu___))::rest -> begin
     (match ((find_column n cs)) with
| FStar_Pervasives_Native.Some (c) -> begin
     (match ((first_uncarriable h c.name c.ctype c.cells)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (e)
     end
| FStar_Pervasives_Native.None -> begin
     (cells_fault h rest cs)
     end)
     end
| FStar_Pervasives_Native.None -> begin
     (cells_fault h rest cs)
     end)
     end))


let validate = (fun ( h  :  host<'num, 'flt> ) ( t  :  table<'num, 'flt> ) -> (

let sn = (schema_names t.schema)
in (

let cn = (column_names t.columns)
in (match ((first_duplicate sn)) with
| FStar_Pervasives_Native.Some (n) -> begin
     Bad (Malformed (DuplicateSchemaName (n)))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((first_duplicate cn)) with
| FStar_Pervasives_Native.Some (n) -> begin
     Bad (Malformed (DuplicateColumnName (n)))
     end
| FStar_Pervasives_Native.None -> begin
      
if (match ((not_in sn cn)) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     Bad (Malformed (SchemaNameWithoutColumn))
     end else begin
      
if (match ((not_in cn sn)) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     Bad (Malformed (ColumnOutsideSchema))
     end else begin
     (match ((type_fault t.schema t.columns)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Bad (e)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((ragged t.columns)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Bad (e)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((cells_fault h t.schema t.columns)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Bad (e)
     end
| FStar_Pervasives_Native.None -> begin
     Good (())
     end)
     end)
     end)
     end
     end
     end)
     end))))


let absent_slot = (fun ( h  :  host<'num, 'flt> ) ( ty  :  column_type ) -> (match (ty) with
| IntType -> begin
     WireCanon.JInt (h.zero_int)
     end
| FloatType -> begin
     WireCanon.JFloat (h.zero_float)
     end
| BoolType -> begin
     WireCanon.JBool (false)
     end
| StringType -> begin
     WireCanon.JStr ([])
     end
| DateType -> begin
     WireCanon.JStr ([])
     end
| TimestampType -> begin
     WireCanon.JStr ([])
     end
| DecimalType -> begin
     WireCanon.JStr (dec_zero)
     end))


let cell_json = (fun ( h  :  host<'num, 'flt> ) ( ty  :  column_type ) ( c  :  cell<'num, 'flt> ) -> (match (c) with
| Null -> begin
     (absent_slot h ty)
     end
| Int (i) -> begin
     (match (ty) with
| FloatType -> begin
     WireCanon.JFloat ((h.to_float i))
     end
| DecimalType -> begin
     WireCanon.JStr ((h.int_text i))
     end
| uu___ -> begin
     WireCanon.JInt (i)
     end)
     end
| Float (f) -> begin
     WireCanon.JFloat (f)
     end
| Bool (b) -> begin
     WireCanon.JBool (b)
     end
| Str (s) -> begin
     WireCanon.JStr (s)
     end
| Date (s) -> begin
     WireCanon.JStr (s)
     end
| Timestamp (s) -> begin
     WireCanon.JStr (s)
     end
| Decimal (s) -> begin
     WireCanon.JStr (s)
     end))


let rec values_json = (fun ( h  :  host<'num, 'flt> ) ( ty  :  column_type ) ( cs  :  Prims.list<cell<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     ((cell_json h ty c))::(values_json h ty t)
     end))


let rec validity_json = (fun ( cs  :  Prims.list<cell<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (WireCanon.JBool ((not ((match (c) with
| Null -> begin
     true
     end
| uu___ -> begin
     false
     end)))))::(validity_json t)
     end))


let column_json = (fun ( h  :  host<'num, 'flt> ) ( c  :  column<'num, 'flt> ) -> WireCanon.JObj ((((values_key), (WireCanon.JArr ((values_json h c.ctype c.cells)))))::(((validity_key), (WireCanon.JArr ((validity_json c.cells)))))::[]))


let rec schema_json_items = (fun ( s  :  Prims.list<(Prims.list<WireCanon.ch> * column_type)> ) -> (match (s) with
| [] -> begin
     []
     end
| ((n, ty))::t -> begin
     (WireCanon.JObj ((((name_key), (WireCanon.JStr (n))))::(((type_key), (WireCanon.JStr ((tag ty)))))::[]))::(schema_json_items t)
     end))


let column_or_placeholder = (fun ( n  :  Prims.list<WireCanon.ch> ) ( cs  :  Prims.list<column<'num, 'flt>> ) -> (match ((find_column n cs)) with
| FStar_Pervasives_Native.Some (c) -> begin
     c
     end
| FStar_Pervasives_Native.None -> begin
     {name = n; ctype = StringType; cells = []}
     end))


let rec columns_json = (fun ( h  :  host<'num, 'flt> ) ( s  :  Prims.list<(Prims.list<WireCanon.ch> * column_type)> ) ( cs  :  Prims.list<column<'num, 'flt>> ) -> (match (s) with
| [] -> begin
     []
     end
| ((n, uu___))::t -> begin
     (((n), ((column_json h (column_or_placeholder n cs)))))::(columns_json h t cs)
     end))


let encode_json = (fun ( h  :  host<'num, 'flt> ) ( src  :  data_source<'num, 'flt> ) -> (match (src) with
| Embedded (t) -> begin
     WireCanon.JObj ((((schema_key), (WireCanon.JArr ((schema_json_items t.schema)))))::(((columns_key), (WireCanon.JObj ((columns_json h t.schema t.columns)))))::[])
     end
| Ref (r) -> begin
     WireCanon.JObj ((((schema_key), (WireCanon.JArr ([]))))::(((ref_key), (WireCanon.JStr (r))))::[])
     end))


let try_encode_json = (fun ( h  :  host<'num, 'flt> ) ( src  :  data_source<'num, 'flt> ) -> (match (src) with
| Ref (uu___) -> begin
     Good ((encode_json h src))
     end
| Embedded (t) -> begin
     (match ((validate h t)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (uu___) -> begin
     Good ((encode_json h src))
     end)
     end))


let rec find_kv = (fun ( n  :  Prims.list<WireCanon.ch> ) ( fs  :  Prims.list<(Prims.list<WireCanon.ch> * WireCanon.jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((k, v))::t -> begin
      
if (Prims.op_Equals k n) then begin
     FStar_Pervasives_Native.Some (v)
     end else begin
     (find_kv n t)
     end
     end))


let try_prop = (fun ( n  :  Prims.list<WireCanon.ch> ) ( el  :  WireCanon.jval<'num, 'flt> ) -> (match (el) with
| WireCanon.JObj (fs) -> begin
     (find_kv n fs)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let get_field = (fun ( n  :  Prims.list<WireCanon.ch> ) ( el  :  WireCanon.jval<'num, 'flt> ) -> (match (el) with
| WireCanon.JObj (fs) -> begin
     (match ((find_kv n fs)) with
| FStar_Pervasives_Native.Some (v) -> begin
     Good (v)
     end
| FStar_Pervasives_Native.None -> begin
     Bad (MissingField (n))
     end)
     end
| uu___ -> begin
     Bad (MalformedShape)
     end))


let as_arr = (fun ( el  :  WireCanon.jval<'num, 'flt> ) -> (match (el) with
| WireCanon.JArr (xs) -> begin
     Good (xs)
     end
| uu___ -> begin
     Bad (MalformedShape)
     end))


let as_str = (fun ( el  :  WireCanon.jval<'num, 'flt> ) -> (match (el) with
| WireCanon.JStr (s) -> begin
     Good (s)
     end
| uu___ -> begin
     Bad (MalformedShape)
     end))


let decode_cell = (fun ( h  :  host<'num, 'flt> ) ( cname  :  Prims.list<WireCanon.ch> ) ( ty  :  column_type ) ( v  :  WireCanon.jval<'num, 'flt> ) -> (match (ty) with
| IntType -> begin
     (match (v) with
| WireCanon.JInt (i) -> begin
     Good (Int (i))
     end
| uu___ -> begin
     Bad (TypeMismatch (cname, ty))
     end)
     end
| FloatType -> begin
     (match (v) with
| WireCanon.JFloat (f) -> begin
     Good (Float (f))
     end
| WireCanon.JInt (i) -> begin
     Good (Float ((h.to_float i)))
     end
| uu___ -> begin
     Bad (TypeMismatch (cname, ty))
     end)
     end
| BoolType -> begin
     (match (v) with
| WireCanon.JBool (b) -> begin
     Good (Bool (b))
     end
| uu___ -> begin
     Bad (TypeMismatch (cname, ty))
     end)
     end
| StringType -> begin
     (match (v) with
| WireCanon.JStr (s) -> begin
     Good (Str (s))
     end
| uu___ -> begin
     Bad (TypeMismatch (cname, ty))
     end)
     end
| DateType -> begin
     (match (v) with
| WireCanon.JStr (s) -> begin
      
if (h.is_date s) then begin
     Good (Date (s))
     end else begin
     Bad (MalformedShape)
     end
     end
| uu___ -> begin
     Bad (TypeMismatch (cname, ty))
     end)
     end
| TimestampType -> begin
     (match (v) with
| WireCanon.JStr (s) -> begin
      
if (h.is_timestamp s) then begin
     Good (Timestamp (s))
     end else begin
     Bad (MalformedShape)
     end
     end
| WireCanon.JInt (uu___) -> begin
     Bad (OutOfModel)
     end
| WireCanon.JFloat (uu___) -> begin
     Bad (OutOfModel)
     end
| uu___ -> begin
     Bad (TypeMismatch (cname, ty))
     end)
     end
| DecimalType -> begin
     (match (v) with
| WireCanon.JInt (i) -> begin
     Good (Decimal ((h.int_text i)))
     end
| WireCanon.JFloat (uu___) -> begin
     Bad (OutOfModel)
     end
| WireCanon.JStr (s) -> begin
     (match ((try_canonical s)) with
| FStar_Pervasives_Native.Some (canonical) -> begin
     Good (Decimal (canonical))
     end
| FStar_Pervasives_Native.None -> begin
     Bad (MalformedShape)
     end)
     end
| uu___ -> begin
     Bad (TypeMismatch (cname, ty))
     end)
     end))


let decode_schema_entry = (fun ( el  :  WireCanon.jval<'num, 'flt> ) -> (match ((get_field name_key el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (name_el) -> begin
     (match ((as_str name_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (n) -> begin
     (match ((get_field type_key el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (type_el) -> begin
     (match ((as_str type_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (tg) -> begin
     (match ((of_tag tg)) with
| FStar_Pervasives_Native.Some (ty) -> begin
     Good (((n), (ty)))
     end
| FStar_Pervasives_Native.None -> begin
     Bad (UnknownType (tg))
     end)
     end)
     end)
     end)
     end))


let rec decode_schema_items = (fun ( xs  :  Prims.list<WireCanon.jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     Good ([])
     end
| (x)::rest -> begin
     (match ((decode_schema_entry x)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (entry) -> begin
     (match ((decode_schema_items rest)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (entries) -> begin
     Good ((entry)::entries)
     end)
     end)
     end))


let decode_schema = (fun ( el  :  WireCanon.jval<'num, 'flt> ) -> (match ((as_arr el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (xs) -> begin
     (decode_schema_items xs)
     end))


let column_parts = (fun ( col_el  :  WireCanon.jval<'num, 'flt> ) -> (match (col_el) with
| WireCanon.JArr (uu___) -> begin
     Bad (OutOfModel)
     end
| uu___ -> begin
     (match ((get_field values_key col_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (values_el) -> begin
     (match ((as_arr values_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (values) -> begin
     (match ((try_prop validity_key col_el)) with
| FStar_Pervasives_Native.None -> begin
     Bad (OutOfModel)
     end
| FStar_Pervasives_Native.Some (validity_el) -> begin
     (match ((as_arr validity_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (validity) -> begin
     Good (((values), (validity)))
     end)
     end)
     end)
     end)
     end))


let rec decode_cells = (fun ( h  :  host<'num, 'flt> ) ( cname  :  Prims.list<WireCanon.ch> ) ( ty  :  column_type ) ( values  :  Prims.list<WireCanon.jval<'num, 'flt>> ) ( validity  :  Prims.list<WireCanon.jval<'num, 'flt>> ) -> (match (((values), (validity))) with
| ([], []) -> begin
     Good ([])
     end
| ((v)::vs, (p)::ps) -> begin
     (match (p) with
| WireCanon.JBool (present) -> begin
      
if (not (present)) then begin
     (match ((decode_cells h cname ty vs ps)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (cs) -> begin
     Good ((Null)::cs)
     end)
     end else begin
     (match ((decode_cell h cname ty v)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (c) -> begin
     (match ((decode_cells h cname ty vs ps)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (cs) -> begin
     Good ((c)::cs)
     end)
     end)
     end
     end
| uu___ -> begin
     Bad (MalformedShape)
     end)
     end
| uu___ -> begin
     Bad (MalformedShape)
     end))


let decode_column = (fun ( h  :  host<'num, 'flt> ) ( columns_obj  :  WireCanon.jval<'num, 'flt> ) ( n  :  Prims.list<WireCanon.ch> ) ( ty  :  column_type ) -> (match ((try_prop n columns_obj)) with
| FStar_Pervasives_Native.None -> begin
     Bad (MissingColumn (n))
     end
| FStar_Pervasives_Native.Some (col_el) -> begin
     (match ((column_parts col_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (values, validity) -> begin
      
if (not ((same_len values validity))) then begin
     Bad (LengthMismatch (n))
     end else begin
     (match ((decode_cells h n ty values validity)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (cs) -> begin
     Good ({name = n; ctype = ty; cells = cs})
     end)
     end
     end)
     end))


let rec decode_columns = (fun ( h  :  host<'num, 'flt> ) ( columns_obj  :  WireCanon.jval<'num, 'flt> ) ( s  :  Prims.list<(Prims.list<WireCanon.ch> * column_type)> ) -> (match (s) with
| [] -> begin
     Good ([])
     end
| ((n, ty))::rest -> begin
     (match ((decode_column h columns_obj n ty)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (c) -> begin
     (match ((decode_columns h columns_obj rest)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (cs) -> begin
     Good ((c)::cs)
     end)
     end)
     end))


let unique_column_keys = (fun ( columns_obj  :  WireCanon.jval<'num, 'flt> ) -> (match (columns_obj) with
| WireCanon.JObj (fs) -> begin
     (match ((first_duplicate (WireCanon.keys_of fs))) with
| FStar_Pervasives_Native.Some (k) -> begin
     Bad (Malformed (DuplicateColumnKey (k)))
     end
| FStar_Pervasives_Native.None -> begin
     Good (columns_obj)
     end)
     end
| uu___ -> begin
     Good (columns_obj)
     end))


let decode_json = (fun ( h  :  host<'num, 'flt> ) ( el  :  WireCanon.jval<'num, 'flt> ) -> (

let schema_r = (match ((try_prop schema_key el)) with
| FStar_Pervasives_Native.Some (schema_el) -> begin
     (match ((decode_schema schema_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (s) -> begin
     Good (FStar_Pervasives_Native.Some (s))
     end)
     end
| FStar_Pervasives_Native.None -> begin
     Good (FStar_Pervasives_Native.None)
     end)
in (match (schema_r) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (schema_opt) -> begin
     (match ((try_prop ref_key el)) with
| FStar_Pervasives_Native.Some (ref_el) -> begin
     (match ((as_str ref_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (r) -> begin
     Good (Ref (r))
     end)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((get_field columns_key el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (columns_el) -> begin
     (match ((unique_column_keys columns_el)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (columns_obj) -> begin
     (match (schema_opt) with
| FStar_Pervasives_Native.None -> begin
     Bad (OutOfModel)
     end
| FStar_Pervasives_Native.Some (s) -> begin
     (match ((decode_columns h columns_obj s)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (cs) -> begin
     (

let t = {schema = s; columns = cs}
in (match ((validate h t)) with
| Bad (e) -> begin
     Bad (e)
     end
| Good (uu___) -> begin
     Good (Embedded (t))
     end))
     end)
     end)
     end)
     end)
     end)
     end)))


let norm_cell = (fun ( h  :  host<'num, 'flt> ) ( ty  :  column_type ) ( c  :  cell<'num, 'flt> ) -> (match (c) with
| Int (i) -> begin
     (match (ty) with
| FloatType -> begin
     Float ((h.to_float i))
     end
| DecimalType -> begin
     Decimal ((h.int_text i))
     end
| uu___ -> begin
     c
     end)
     end
| uu___ -> begin
     c
     end))


let rec norm_cells = (fun ( h  :  host<'num, 'flt> ) ( ty  :  column_type ) ( cs  :  Prims.list<cell<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     ((norm_cell h ty c))::(norm_cells h ty t)
     end))


let normal_column = (fun ( h  :  host<'num, 'flt> ) ( n  :  Prims.list<WireCanon.ch> ) ( ty  :  column_type ) ( cs  :  Prims.list<column<'num, 'flt>> ) -> {name = n; ctype = ty; cells = (norm_cells h ty (column_or_placeholder n cs).cells)})


let rec normal_columns = (fun ( h  :  host<'num, 'flt> ) ( s  :  Prims.list<(Prims.list<WireCanon.ch> * column_type)> ) ( cs  :  Prims.list<column<'num, 'flt>> ) -> (match (s) with
| [] -> begin
     []
     end
| ((n, ty))::t -> begin
     ((normal_column h n ty cs))::(normal_columns h t cs)
     end))


let normal_table = (fun ( h  :  host<'num, 'flt> ) ( t  :  table<'num, 'flt> ) -> {schema = t.schema; columns = (normal_columns h t.schema t.columns)})


let normal_source = (fun ( h  :  host<'num, 'flt> ) ( src  :  data_source<'num, 'flt> ) -> (match (src) with
| Embedded (t) -> begin
     Embedded ((normal_table h t))
     end
| Ref (r) -> begin
     Ref (r)
     end))


let single_column_table = (fun ( n  :  Prims.list<WireCanon.ch> ) ( ty  :  column_type ) ( cs  :  Prims.list<cell<'num, 'flt>> ) -> {schema = (((n), (ty)))::[]; columns = ({name = n; ctype = ty; cells = cs})::[]})


let reordered_table = (fun ( a  :  Prims.list<WireCanon.ch> ) ( b  :  Prims.list<WireCanon.ch> ) -> {schema = (((a), (IntType)))::(((b), (IntType)))::[]; columns = ({name = b; ctype = IntType; cells = []})::({name = a; ctype = IntType; cells = []})::[]})

type twin = {tname : Prims.string; tholds : unit  ->  Prims.bool}


let __proj__Mktwin__item__tname : twin  ->  Prims.string = (fun ( projectee  :  twin ) -> (match (projectee) with
| {tname = tname; tholds = tholds} -> begin
     tname
     end))


let __proj__Mktwin__item__tholds : twin  ->  unit  ->  Prims.bool = (fun ( projectee  :  twin ) -> (match (projectee) with
| {tname = tname; tholds = tholds} -> begin
     tholds
     end))


let rec twins_hold : Prims.list<twin>  ->  Prims.bool = (fun ( l  :  Prims.list<twin> ) -> (match (l) with
| [] -> begin
     true
     end
| (t)::r -> begin
     ((t.tholds ()) && (twins_hold r))
     end))


let twins : Prims.list<twin> = ({tname = "try-canonical-trims-zeros"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (try_canonical ((WireCanon.CHexCh (WireCanon.HD0))::(WireCanon.CHexCh (WireCanon.HD1))::(WireCanon.CDot)::(WireCanon.CHexCh (WireCanon.HD5))::(WireCanon.CHexCh (WireCanon.HD0))::[])) (FStar_Pervasives_Native.Some ((WireCanon.CHexCh (WireCanon.HD1))::(WireCanon.CDot)::(WireCanon.CHexCh (WireCanon.HD5))::[]))))})::({tname = "try-canonical-refuses-a-bare-point"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (try_canonical ((WireCanon.CHexCh (WireCanon.HD1))::(WireCanon.CDot)::[])) FStar_Pervasives_Native.None))})::({tname = "of-tag-reads-int"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (of_tag ((WireCanon.CPlain ("i"))::(WireCanon.CPlain ("n"))::(WireCanon.CPlain ("t"))::[])) (FStar_Pervasives_Native.Some (IntType))))})::[]




