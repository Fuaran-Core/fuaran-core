module ColumnRefinement

type validity = Prims.list<Prims.bool>

type column_data<'num, 'flt> =
| Ints of Prims.list<'num> * validity
| Floats of Prims.list<'flt> * validity
| Bools of Prims.list<Prims.bool> * validity
| Strs of Prims.list<Prims.list<WireCanon.ch>> * validity
| Dates of Prims.list<Prims.int> * validity
| Timestamps of Temporal.time_unit * Prims.list<Prims.int> * FStar_Pervasives_Native.option<Prims.list<Prims.int>> * validity
| Decimals of Prims.list<Prims.list<WireCanon.ch>> * validity


let uu___is_Ints = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Ints (values, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ints__item__values = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Ints (values, mask) -> begin
     values
     end))


let __proj__Ints__item__mask = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Ints (values, mask) -> begin
     mask
     end))


let uu___is_Floats = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Floats (values, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Floats__item__values = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Floats (values, mask) -> begin
     values
     end))


let __proj__Floats__item__mask = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Floats (values, mask) -> begin
     mask
     end))


let uu___is_Bools = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Bools (values, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Bools__item__values = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Bools (values, mask) -> begin
     values
     end))


let __proj__Bools__item__mask = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Bools (values, mask) -> begin
     mask
     end))


let uu___is_Strs = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Strs (values, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Strs__item__values = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Strs (values, mask) -> begin
     values
     end))


let __proj__Strs__item__mask = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Strs (values, mask) -> begin
     mask
     end))


let uu___is_Dates = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Dates (values, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Dates__item__values = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Dates (values, mask) -> begin
     values
     end))


let __proj__Dates__item__mask = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Dates (values, mask) -> begin
     mask
     end))


let uu___is_Timestamps = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Timestamps (u, values, fraction, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Timestamps__item__u = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Timestamps (u, values, fraction, mask) -> begin
     u
     end))


let __proj__Timestamps__item__values = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Timestamps (u, values, fraction, mask) -> begin
     values
     end))


let __proj__Timestamps__item__fraction = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Timestamps (u, values, fraction, mask) -> begin
     fraction
     end))


let __proj__Timestamps__item__mask = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Timestamps (u, values, fraction, mask) -> begin
     mask
     end))


let uu___is_Decimals = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Decimals (values, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Decimals__item__values = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Decimals (values, mask) -> begin
     values
     end))


let __proj__Decimals__item__mask = (fun ( projectee  :  column_data<'num, 'flt> ) -> (match (projectee) with
| Decimals (values, mask) -> begin
     mask
     end))


let data_type = (fun ( d  :  column_data<'num, 'flt> ) -> (match (d) with
| Ints (uu___, uu___1) -> begin
     WireColumn.IntType
     end
| Floats (uu___, uu___1) -> begin
     WireColumn.FloatType
     end
| Bools (uu___, uu___1) -> begin
     WireColumn.BoolType
     end
| Strs (uu___, uu___1) -> begin
     WireColumn.StringType
     end
| Dates (uu___, uu___1) -> begin
     WireColumn.DateType
     end
| Timestamps (u, uu___, uu___1, uu___2) -> begin
     WireColumn.TimestampType (u)
     end
| Decimals (uu___, uu___1) -> begin
     WireColumn.DecimalType
     end))


let data_mask = (fun ( d  :  column_data<'num, 'flt> ) -> (match (d) with
| Ints (uu___, v) -> begin
     v
     end
| Floats (uu___, v) -> begin
     v
     end
| Bools (uu___, v) -> begin
     v
     end
| Strs (uu___, v) -> begin
     v
     end
| Dates (uu___, v) -> begin
     v
     end
| Timestamps (uu___, uu___1, uu___2, v) -> begin
     v
     end
| Decimals (uu___, v) -> begin
     v
     end))


let rec units = (fun ( xs  :  Prims.list<'a> ) -> (match (xs) with
| [] -> begin
     []
     end
| (uu___)::t -> begin
     (())::(units t)
     end))


let data_len = (fun ( d  :  column_data<'num, 'flt> ) -> (match (d) with
| Ints (xs, uu___) -> begin
     (units xs)
     end
| Floats (xs, uu___) -> begin
     (units xs)
     end
| Bools (xs, uu___) -> begin
     (units xs)
     end
| Strs (xs, uu___) -> begin
     (units xs)
     end
| Dates (xs, uu___) -> begin
     (units xs)
     end
| Timestamps (uu___, xs, uu___1, uu___2) -> begin
     (units xs)
     end
| Decimals (xs, uu___) -> begin
     (units xs)
     end))


let data_wf = (fun ( d  :  column_data<'num, 'flt> ) -> (match (d) with
| Ints (xs, v) -> begin
     (WireColumn.same_len xs v)
     end
| Floats (xs, v) -> begin
     (WireColumn.same_len xs v)
     end
| Bools (xs, v) -> begin
     (WireColumn.same_len xs v)
     end
| Strs (xs, v) -> begin
     (WireColumn.same_len xs v)
     end
| Dates (xs, v) -> begin
     (WireColumn.same_len xs v)
     end
| Timestamps (uu___, xs, uu___1, v) -> begin
     (WireColumn.same_len xs v)
     end
| Decimals (xs, v) -> begin
     (WireColumn.same_len xs v)
     end))

type typed_column<'num, 'flt> = {col_name : Prims.list<WireCanon.ch>; col_data : column_data<'num, 'flt>}


let __proj__Mktyped_column__item__col_name = (fun ( projectee  :  typed_column<'num, 'flt> ) -> (match (projectee) with
| {col_name = col_name; col_data = col_data} -> begin
     col_name
     end))


let __proj__Mktyped_column__item__col_data = (fun ( projectee  :  typed_column<'num, 'flt> ) -> (match (projectee) with
| {col_name = col_name; col_data = col_data} -> begin
     col_data
     end))


let col_type = (fun ( c  :  typed_column<'num, 'flt> ) -> (data_type c.col_data))


let col_mask = (fun ( c  :  typed_column<'num, 'flt> ) -> (data_mask c.col_data))


let wf = (fun ( c  :  typed_column<'num, 'flt> ) -> (data_wf c.col_data))

type typed_table<'num, 'flt> = {tschema : Prims.list<(Prims.list<WireCanon.ch> * WireColumn.column_type)>; tcolumns : Prims.list<typed_column<'num, 'flt>>}


let __proj__Mktyped_table__item__tschema = (fun ( projectee  :  typed_table<'num, 'flt> ) -> (match (projectee) with
| {tschema = tschema; tcolumns = tcolumns} -> begin
     tschema
     end))


let __proj__Mktyped_table__item__tcolumns = (fun ( projectee  :  typed_table<'num, 'flt> ) -> (match (projectee) with
| {tschema = tschema; tcolumns = tcolumns} -> begin
     tcolumns
     end))

type typed_source<'num, 'flt> =
| TEmbedded of typed_table<'num, 'flt>
| TRef of Prims.list<WireCanon.ch>


let uu___is_TEmbedded = (fun ( projectee  :  typed_source<'num, 'flt> ) -> (match (projectee) with
| TEmbedded (t) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TEmbedded__item__t = (fun ( projectee  :  typed_source<'num, 'flt> ) -> (match (projectee) with
| TEmbedded (t) -> begin
     t
     end))


let uu___is_TRef = (fun ( projectee  :  typed_source<'num, 'flt> ) -> (match (projectee) with
| TRef (r) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TRef__item__r = (fun ( projectee  :  typed_source<'num, 'flt> ) -> (match (projectee) with
| TRef (r) -> begin
     r
     end))


let rec all_wf = (fun ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((wf c) && (all_wf t))
     end))


let rec cells_of = (fun ( mk  :  'a  ->  WireColumn.cell<'num, 'flt> ) ( xs  :  Prims.list<'a> ) ( v  :  validity ) -> (match (xs) with
| [] -> begin
     []
     end
| (x)::xt -> begin
     (match (v) with
| (true)::vt -> begin
     ((mk x))::(cells_of mk xt vt)
     end
| (false)::vt -> begin
     (WireColumn.Null)::(cells_of mk xt vt)
     end
| [] -> begin
     (WireColumn.Null)::(cells_of mk xt [])
     end)
     end))


let rec zip_frac : Prims.list<Prims.int>  ->  Prims.list<Prims.int>  ->  Prims.list<(Prims.int * Prims.int)> = (fun ( xs  :  Prims.list<Prims.int> ) ( fs  :  Prims.list<Prims.int> ) -> (match (xs) with
| [] -> begin
     []
     end
| (x)::xt -> begin
     (match (fs) with
| (f)::ft -> begin
     (((x), (f)))::(zip_frac xt ft)
     end
| [] -> begin
     (((x), ((Prims.parse_int "0"))))::(zip_frac xt [])
     end)
     end))


let pairs : Prims.list<Prims.int>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.int>>  ->  Prims.list<(Prims.int * Prims.int)> = (fun ( xs  :  Prims.list<Prims.int> ) ( f  :  FStar_Pervasives_Native.option<Prims.list<Prims.int>> ) -> (match (f) with
| FStar_Pervasives_Native.Some (fs) -> begin
     (zip_frac xs fs)
     end
| FStar_Pervasives_Native.None -> begin
     (zip_frac xs [])
     end))


let date_cell = (fun ( d  :  Prims.int ) -> WireColumn.Date ((Temporal.date_text d)))


let ts_cell = (fun ( u  :  Temporal.time_unit ) ( p  :  (Prims.int * Prims.int) ) -> WireColumn.Timestamp ((Temporal.instant_text u (FStar_Pervasives_Native.fst p) (FStar_Pervasives_Native.snd p))))


let to_cells = (fun ( c  :  typed_column<'num, 'flt> ) -> (match (c.col_data) with
| Ints (xs, v) -> begin
     (cells_of (fun ( uu___  :  'num ) -> WireColumn.Int (uu___)) xs v)
     end
| Floats (xs, v) -> begin
     (cells_of (fun ( uu___  :  'flt ) -> WireColumn.Float (uu___)) xs v)
     end
| Bools (xs, v) -> begin
     (cells_of (fun ( uu___  :  Prims.bool ) -> WireColumn.Bool (uu___)) xs v)
     end
| Strs (xs, v) -> begin
     (cells_of (fun ( uu___  :  Prims.list<WireCanon.ch> ) -> WireColumn.Str (uu___)) xs v)
     end
| Dates (xs, v) -> begin
     (cells_of date_cell xs v)
     end
| Timestamps (u, xs, f, v) -> begin
     (cells_of (ts_cell u) (pairs xs f) v)
     end
| Decimals (xs, v) -> begin
     (cells_of (fun ( uu___  :  Prims.list<WireCanon.ch> ) -> WireColumn.Decimal (uu___)) xs v)
     end))


let to_list_column = (fun ( c  :  typed_column<'num, 'flt> ) -> {WireColumn.name = c.col_name; WireColumn.ctype = (col_type c); WireColumn.cells = (to_cells c)})


let rec to_list_columns = (fun ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     ((to_list_column c))::(to_list_columns t)
     end))


let to_list_table = (fun ( t  :  typed_table<'num, 'flt> ) -> {WireColumn.schema = t.tschema; WireColumn.columns = (to_list_columns t.tcolumns)})


let to_list_source = (fun ( src  :  typed_source<'num, 'flt> ) -> (match (src) with
| TEmbedded (t) -> begin
     WireColumn.Embedded ((to_list_table t))
     end
| TRef (r) -> begin
     WireColumn.Ref (r)
     end))

type picked<'a> =
| Fits of 'a
| Absent
| Outside of WireColumn.column_type
| Unreadable


let uu___is_Fits = (fun ( projectee  :  picked<'a> ) -> (match (projectee) with
| Fits (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Fits__item__v = (fun ( projectee  :  picked<'a> ) -> (match (projectee) with
| Fits (v) -> begin
     v
     end))


let uu___is_Absent = (fun ( projectee  :  picked<'a> ) -> (match (projectee) with
| Absent -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Outside = (fun ( projectee  :  picked<'a> ) -> (match (projectee) with
| Outside (t) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Outside__item__t = (fun ( projectee  :  picked<'a> ) -> (match (projectee) with
| Outside (t) -> begin
     t
     end))


let uu___is_Unreadable = (fun ( projectee  :  picked<'a> ) -> (match (projectee) with
| Unreadable -> begin
     true
     end
| uu___ -> begin
     false
     end))


let outside = (fun ( c  :  WireColumn.cell<'num, 'flt> ) -> (match ((WireColumn.type_of c)) with
| FStar_Pervasives_Native.Some (t) -> begin
     Outside (t)
     end
| FStar_Pervasives_Native.None -> begin
     Absent
     end))


let pick_int = (fun ( c  :  WireColumn.cell<'num, 'flt> ) -> (match (c) with
| WireColumn.Int (i) -> begin
     Fits (i)
     end
| uu___ -> begin
     (outside c)
     end))


let pick_float = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( c  :  WireColumn.cell<'num, 'flt> ) -> (match (c) with
| WireColumn.Float (f) -> begin
     Fits (f)
     end
| WireColumn.Int (i) -> begin
     Fits ((h.to_float i))
     end
| uu___ -> begin
     (outside c)
     end))


let pick_bool = (fun ( c  :  WireColumn.cell<'num, 'flt> ) -> (match (c) with
| WireColumn.Bool (b) -> begin
     Fits (b)
     end
| uu___ -> begin
     (outside c)
     end))


let pick_str = (fun ( c  :  WireColumn.cell<'num, 'flt> ) -> (match (c) with
| WireColumn.Str (s) -> begin
     Fits (s)
     end
| uu___ -> begin
     (outside c)
     end))


let pick_date = (fun ( c  :  WireColumn.cell<'num, 'flt> ) -> (match (c) with
| WireColumn.Date (s) -> begin
     (match ((Temporal.try_days s)) with
| FStar_Pervasives_Native.Some (d) -> begin
     Fits (d)
     end
| FStar_Pervasives_Native.None -> begin
     Unreadable
     end)
     end
| uu___ -> begin
     (outside c)
     end))


let pick_timestamp = (fun ( u  :  Temporal.time_unit ) ( c  :  WireColumn.cell<'num, 'flt> ) -> (match (c) with
| WireColumn.Timestamp (s) -> begin
      
if (not ((Temporal.is_canonical_timestamp s))) then begin
     Unreadable
     end else begin
     (match ((Temporal.try_instant u s)) with
| FStar_Pervasives_Native.Some (p) -> begin
     Fits (p)
     end
| FStar_Pervasives_Native.None -> begin
     (outside c)
     end)
     end
     end
| uu___ -> begin
     (outside c)
     end))


let pick_decimal = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( c  :  WireColumn.cell<'num, 'flt> ) -> (match (c) with
| WireColumn.Decimal (s) -> begin
     Fits (s)
     end
| WireColumn.Int (i) -> begin
     Fits ((h.int_text i))
     end
| uu___ -> begin
     (outside c)
     end))


let rec fill = (fun ( name  :  Prims.list<WireCanon.ch> ) ( ty  :  WireColumn.column_type ) ( zero  :  'a ) ( pick  :  WireColumn.cell<'num, 'flt>  ->  picked<'a> ) ( cs  :  Prims.list<WireColumn.cell<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     WireColumn.Good ((([]), ([])))
     end
| (c)::t -> begin
     (match ((pick c)) with
| Outside (uu___) -> begin
     WireColumn.Bad (WireColumn.TypeMismatch (name, ty))
     end
| Unreadable -> begin
     WireColumn.Bad (WireColumn.MalformedShape)
     end
| Fits (v) -> begin
     (match ((fill name ty zero pick t)) with
| WireColumn.Good (xs, m) -> begin
     WireColumn.Good ((((v)::xs), ((true)::m)))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end
| Absent -> begin
     (match ((fill name ty zero pick t)) with
| WireColumn.Good (xs, m) -> begin
     WireColumn.Good ((((zero)::xs), ((false)::m)))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end)
     end))


let rec firsts : Prims.list<(Prims.int * Prims.int)>  ->  Prims.list<Prims.int> = (fun ( ps  :  Prims.list<(Prims.int * Prims.int)> ) -> (match (ps) with
| [] -> begin
     []
     end
| ((x, uu___))::t -> begin
     (x)::(firsts t)
     end))


let rec seconds : Prims.list<(Prims.int * Prims.int)>  ->  Prims.list<Prims.int> = (fun ( ps  :  Prims.list<(Prims.int * Prims.int)> ) -> (match (ps) with
| [] -> begin
     []
     end
| ((uu___, f))::t -> begin
     (f)::(seconds t)
     end))


let rec any_present_nonzero : Prims.list<Prims.int>  ->  validity  ->  Prims.bool = (fun ( fs  :  Prims.list<Prims.int> ) ( v  :  validity ) -> (match (fs) with
| [] -> begin
     false
     end
| (f)::ft -> begin
     (match (v) with
| (true)::vt -> begin
     ((Prims.op_Less_Greater f (Prims.parse_int "0")) || (any_present_nonzero ft vt))
     end
| (false)::vt -> begin
     (any_present_nonzero ft vt)
     end
| [] -> begin
     false
     end)
     end))


let normal_fraction : Temporal.time_unit  ->  Prims.list<Prims.int>  ->  validity  ->  FStar_Pervasives_Native.option<Prims.list<Prims.int>> = (fun ( u  :  Temporal.time_unit ) ( fs  :  Prims.list<Prims.int> ) ( v  :  validity ) -> (match (u) with
| Temporal.Seconds -> begin
     FStar_Pervasives_Native.None
     end
| uu___ -> begin
      
if (not ((any_present_nonzero fs v))) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (fs)
     end
     end))


let storage_of_cells = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( name  :  Prims.list<WireCanon.ch> ) ( ty  :  WireColumn.column_type ) ( cs  :  Prims.list<WireColumn.cell<'num, 'flt>> ) -> (match (ty) with
| WireColumn.IntType -> begin
     (match ((fill name ty h.zero_int pick_int cs)) with
| WireColumn.Good (xs, m) -> begin
     WireColumn.Good (Ints (xs, m))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end
| WireColumn.FloatType -> begin
     (match ((fill name ty h.zero_float (pick_float h) cs)) with
| WireColumn.Good (xs, m) -> begin
     WireColumn.Good (Floats (xs, m))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end
| WireColumn.BoolType -> begin
     (match ((fill name ty false pick_bool cs)) with
| WireColumn.Good (xs, m) -> begin
     WireColumn.Good (Bools (xs, m))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end
| WireColumn.StringType -> begin
     (match ((fill name ty [] pick_str cs)) with
| WireColumn.Good (xs, m) -> begin
     WireColumn.Good (Strs (xs, m))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end
| WireColumn.DateType -> begin
     (match ((fill name ty (Prims.parse_int "0") pick_date cs)) with
| WireColumn.Good (xs, m) -> begin
     WireColumn.Good (Dates (xs, m))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end
| WireColumn.TimestampType (u) -> begin
     (match ((fill name ty (((Prims.parse_int "0")), ((Prims.parse_int "0"))) (pick_timestamp u) cs)) with
| WireColumn.Good (ps, m) -> begin
     WireColumn.Good (Timestamps (u, (firsts ps), (normal_fraction u (seconds ps) m), m))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end
| WireColumn.DecimalType -> begin
     (match ((fill name ty WireColumn.dec_zero (pick_decimal h) cs)) with
| WireColumn.Good (xs, m) -> begin
     WireColumn.Good (Decimals (xs, m))
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end)
     end))


let of_cells = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( name  :  Prims.list<WireCanon.ch> ) ( ty  :  WireColumn.column_type ) ( cs  :  Prims.list<WireColumn.cell<'num, 'flt>> ) -> (match ((storage_of_cells h name ty cs)) with
| WireColumn.Good (d) -> begin
     WireColumn.Good ({col_name = name; col_data = d})
     end
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end))


let rec eq_at_present = (fun ( xs  :  Prims.list<'a> ) ( ys  :  Prims.list<'a> ) ( v  :  validity ) -> (match (((xs), (ys))) with
| ((x)::xt, (y)::yt) -> begin
     (match (v) with
| (true)::vt -> begin
     ((Prims.op_Equals x y) && (eq_at_present xt yt vt))
     end
| (false)::vt -> begin
     (eq_at_present xt yt vt)
     end
| [] -> begin
     true
     end)
     end
| uu___ -> begin
     true
     end))


let present_equal = (fun ( xs  :  Prims.list<'a> ) ( vx  :  validity ) ( ys  :  Prims.list<'a> ) ( vy  :  validity ) -> (((WireColumn.same_len xs ys) && (Prims.op_Equals vx vy)) && (eq_at_present xs ys vx)))


let rec materialise : Prims.list<unit>  ->  Prims.list<Prims.int>  ->  Prims.list<Prims.int> = (fun ( n  :  Prims.list<unit> ) ( fs  :  Prims.list<Prims.int> ) -> (match (n) with
| [] -> begin
     []
     end
| (uu___)::nt -> begin
     (match (fs) with
| (f)::ft -> begin
     (f)::(materialise nt ft)
     end
| [] -> begin
     ((Prims.parse_int "0"))::(materialise nt [])
     end)
     end))


let frac_list : Prims.list<unit>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.int>>  ->  Prims.list<Prims.int> = (fun ( n  :  Prims.list<unit> ) ( f  :  FStar_Pervasives_Native.option<Prims.list<Prims.int>> ) -> (match (f) with
| FStar_Pervasives_Native.Some (fs) -> begin
     (materialise n fs)
     end
| FStar_Pervasives_Native.None -> begin
     (materialise n [])
     end))


let same_fraction : Prims.list<unit>  ->  validity  ->  FStar_Pervasives_Native.option<Prims.list<Prims.int>>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.int>>  ->  Prims.bool = (fun ( n  :  Prims.list<unit> ) ( vx  :  validity ) ( fx  :  FStar_Pervasives_Native.option<Prims.list<Prims.int>> ) ( fy  :  FStar_Pervasives_Native.option<Prims.list<Prims.int>> ) -> (match (((fx), (fy))) with
| (FStar_Pervasives_Native.None, FStar_Pervasives_Native.None) -> begin
     true
     end
| uu___ -> begin
     (eq_at_present (frac_list n fx) (frac_list n fy) vx)
     end))


let data_eq = (fun ( d  :  column_data<'num, 'flt> ) ( e  :  column_data<'num, 'flt> ) -> (match (((d), (e))) with
| (Ints (xs, vx), Ints (ys, vy)) -> begin
     (present_equal xs vx ys vy)
     end
| (Floats (xs, vx), Floats (ys, vy)) -> begin
     (present_equal xs vx ys vy)
     end
| (Bools (xs, vx), Bools (ys, vy)) -> begin
     (present_equal xs vx ys vy)
     end
| (Strs (xs, vx), Strs (ys, vy)) -> begin
     (present_equal xs vx ys vy)
     end
| (Dates (xs, vx), Dates (ys, vy)) -> begin
     (present_equal xs vx ys vy)
     end
| (Timestamps (ux, xs, fx, vx), Timestamps (uy, ys, fy, vy)) -> begin
     (((Prims.op_Equals ux uy) && (present_equal xs vx ys vy)) && (same_fraction (units xs) vx fx fy))
     end
| (Decimals (xs, vx), Decimals (ys, vy)) -> begin
     (present_equal xs vx ys vy)
     end
| uu___ -> begin
     false
     end))


let col_eq = (fun ( c  :  typed_column<'num, 'flt> ) ( d  :  typed_column<'num, 'flt> ) -> ((Prims.op_Equals c.col_name d.col_name) && (data_eq c.col_data d.col_data)))


let rec first_present_bad = (fun ( bad  :  'a  ->  Prims.bool ) ( xs  :  Prims.list<'a> ) ( v  :  validity ) -> (match (xs) with
| [] -> begin
     false
     end
| (x)::xt -> begin
     (match (v) with
| (true)::vt -> begin
      
if (bad x) then begin
     true
     end else begin
     (first_present_bad bad xt vt)
     end
     end
| (false)::vt -> begin
     (first_present_bad bad xt vt)
     end
| [] -> begin
     false
     end)
     end))


let not_finite = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( f  :  'flt ) -> (not ((h.finite f))))


let not_canonical : Prims.list<WireCanon.ch>  ->  Prims.bool = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (not ((WireColumn.is_canonical s))))


let day_out : Prims.int  ->  Prims.bool = (fun ( d  :  Prims.int ) -> (not ((Temporal.is_day_in_range d))))


let instant_out : Temporal.time_unit  ->  (Prims.int * Prims.int)  ->  Prims.bool = (fun ( u  :  Temporal.time_unit ) ( p  :  (Prims.int * Prims.int) ) -> (not ((Temporal.is_instant_in_range u (FStar_Pervasives_Native.fst p) (FStar_Pervasives_Native.snd p)))))


let frac_ragged : Prims.list<Prims.int>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.int>>  ->  Prims.bool = (fun ( xs  :  Prims.list<Prims.int> ) ( f  :  FStar_Pervasives_Native.option<Prims.list<Prims.int>> ) -> (match (f) with
| FStar_Pervasives_Native.Some (fs) -> begin
     (not ((WireColumn.same_len fs xs)))
     end
| FStar_Pervasives_Native.None -> begin
     false
     end))


let first_uncarriable_t = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( c  :  typed_column<'num, 'flt> ) ->  
if (not ((wf c))) then begin
     FStar_Pervasives_Native.Some (WireColumn.LengthMismatch (c.col_name))
     end else begin
     (match (c.col_data) with
| Ints (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end
| Bools (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end
| Strs (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end
| Floats (xs, v) -> begin
      
if (first_present_bad (not_finite h) xs v) then begin
     FStar_Pervasives_Native.Some (WireColumn.NonFiniteFloat (c.col_name))
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| Decimals (xs, v) -> begin
      
if (first_present_bad not_canonical xs v) then begin
     FStar_Pervasives_Native.Some (WireColumn.MalformedShape)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| Dates (xs, v) -> begin
      
if (first_present_bad day_out xs v) then begin
     FStar_Pervasives_Native.Some (WireColumn.MalformedShape)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| Timestamps (u, xs, f, v) -> begin
      
if (frac_ragged xs f) then begin
     FStar_Pervasives_Native.Some (WireColumn.MalformedShape)
     end else begin
      
if (first_present_bad (instant_out u) (pairs xs f) v) then begin
     FStar_Pervasives_Native.Some (WireColumn.MalformedShape)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
     end)
     end)


let rec t_column_names = (fun ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (c.col_name)::(t_column_names t)
     end))


let rec t_find_column = (fun ( n  :  Prims.list<WireCanon.ch> ) ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if (Prims.op_Equals c.col_name n) then begin
     FStar_Pervasives_Native.Some (c)
     end else begin
     (t_find_column n t)
     end
     end))


let rec t_type_fault = (fun ( s  :  Prims.list<(Prims.list<WireCanon.ch> * WireColumn.column_type)> ) ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, ty))::rest -> begin
     (match ((t_find_column n cs)) with
| FStar_Pervasives_Native.Some (c) -> begin
      
if (Prims.op_Less_Greater (col_type c) ty) then begin
     FStar_Pervasives_Native.Some (WireColumn.TypeMismatch (n, ty))
     end else begin
     (t_type_fault rest cs)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (t_type_fault rest cs)
     end)
     end))


let rec t_first_ragged = (fun ( len0  :  Prims.list<unit> ) ( rest  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (rest) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if (WireColumn.same_len len0 (data_len c.col_data)) then begin
     (t_first_ragged len0 t)
     end else begin
     FStar_Pervasives_Native.Some (WireColumn.RaggedColumns (c.col_name))
     end
     end))


let t_ragged = (fun ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (first)::rest -> begin
     (t_first_ragged (data_len first.col_data) rest)
     end))


let rec t_cells_fault = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( s  :  Prims.list<(Prims.list<WireCanon.ch> * WireColumn.column_type)> ) ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, uu___))::rest -> begin
     (match ((t_find_column n cs)) with
| FStar_Pervasives_Native.Some (c) -> begin
     (match ((first_uncarriable_t h c)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (e)
     end
| FStar_Pervasives_Native.None -> begin
     (t_cells_fault h rest cs)
     end)
     end
| FStar_Pervasives_Native.None -> begin
     (t_cells_fault h rest cs)
     end)
     end))


let validate_t = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( t  :  typed_table<'num, 'flt> ) -> (

let sn = (WireColumn.schema_names t.tschema)
in (

let cn = (t_column_names t.tcolumns)
in (match ((WireColumn.first_duplicate sn)) with
| FStar_Pervasives_Native.Some (n) -> begin
     WireColumn.Bad (WireColumn.Malformed (WireColumn.DuplicateSchemaName (n)))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((WireColumn.first_duplicate cn)) with
| FStar_Pervasives_Native.Some (n) -> begin
     WireColumn.Bad (WireColumn.Malformed (WireColumn.DuplicateColumnName (n)))
     end
| FStar_Pervasives_Native.None -> begin
      
if (match ((WireColumn.not_in sn cn)) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     WireColumn.Bad (WireColumn.Malformed (WireColumn.SchemaNameWithoutColumn))
     end else begin
      
if (match ((WireColumn.not_in cn sn)) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     WireColumn.Bad (WireColumn.Malformed (WireColumn.ColumnOutsideSchema))
     end else begin
     (match ((t_type_fault t.tschema t.tcolumns)) with
| FStar_Pervasives_Native.Some (e) -> begin
     WireColumn.Bad (e)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((t_ragged t.tcolumns)) with
| FStar_Pervasives_Native.Some (e) -> begin
     WireColumn.Bad (e)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((t_cells_fault h t.tschema t.tcolumns)) with
| FStar_Pervasives_Native.Some (e) -> begin
     WireColumn.Bad (e)
     end
| FStar_Pervasives_Native.None -> begin
     WireColumn.Good (())
     end)
     end)
     end)
     end
     end
     end)
     end))))


let rec values_json_t = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( ty  :  WireColumn.column_type ) ( wrap  :  'a  ->  WireCanon.jval<'num, 'flt> ) ( xs  :  Prims.list<'a> ) ( v  :  validity ) -> (match (xs) with
| [] -> begin
     []
     end
| (x)::xt -> begin
     (match (v) with
| (true)::vt -> begin
     ((wrap x))::(values_json_t h ty wrap xt vt)
     end
| (false)::vt -> begin
     ((WireColumn.absent_slot h ty))::(values_json_t h ty wrap xt vt)
     end
| [] -> begin
     ((WireColumn.absent_slot h ty))::(values_json_t h ty wrap xt [])
     end)
     end))


let rec validity_json_t = (fun ( n  :  Prims.list<unit> ) ( v  :  validity ) -> (match (n) with
| [] -> begin
     []
     end
| (uu___)::nt -> begin
     (match (v) with
| (b)::vt -> begin
     (WireCanon.JBool (b))::(validity_json_t nt vt)
     end
| [] -> begin
     (WireCanon.JBool (false))::(validity_json_t nt [])
     end)
     end))


let date_json = (fun ( d  :  Prims.int ) -> WireCanon.JStr ((Temporal.date_text d)))


let ts_json = (fun ( u  :  Temporal.time_unit ) ( p  :  (Prims.int * Prims.int) ) -> WireCanon.JStr ((Temporal.instant_text u (FStar_Pervasives_Native.fst p) (FStar_Pervasives_Native.snd p))))


let column_json_t = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( c  :  typed_column<'num, 'flt> ) -> (

let values = (match (c.col_data) with
| Ints (xs, v) -> begin
     (values_json_t h WireColumn.IntType (fun ( uu___  :  'num ) -> WireCanon.JInt (uu___)) xs v)
     end
| Floats (xs, v) -> begin
     (values_json_t h WireColumn.FloatType (fun ( uu___  :  'flt ) -> WireCanon.JFloat (uu___)) xs v)
     end
| Bools (xs, v) -> begin
     (values_json_t h WireColumn.BoolType (fun ( uu___  :  Prims.bool ) -> WireCanon.JBool (uu___)) xs v)
     end
| Strs (xs, v) -> begin
     (values_json_t h WireColumn.StringType (fun ( uu___  :  Prims.list<WireCanon.ch> ) -> WireCanon.JStr (uu___)) xs v)
     end
| Dates (xs, v) -> begin
     (values_json_t h WireColumn.DateType date_json xs v)
     end
| Timestamps (u, xs, f, v) -> begin
     (values_json_t h (WireColumn.TimestampType (u)) (ts_json u) (pairs xs f) v)
     end
| Decimals (xs, v) -> begin
     (values_json_t h WireColumn.DecimalType (fun ( uu___  :  Prims.list<WireCanon.ch> ) -> WireCanon.JStr (uu___)) xs v)
     end)
in WireCanon.JObj ((((WireColumn.values_key), (WireCanon.JArr (values))))::(((WireColumn.validity_key), (WireCanon.JArr ((validity_json_t (data_len c.col_data) (col_mask c))))))::[])))


let t_column_or_placeholder = (fun ( n  :  Prims.list<WireCanon.ch> ) ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match ((t_find_column n cs)) with
| FStar_Pervasives_Native.Some (c) -> begin
     c
     end
| FStar_Pervasives_Native.None -> begin
     {col_name = n; col_data = Strs ([], [])}
     end))


let rec columns_json_t = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( s  :  Prims.list<(Prims.list<WireCanon.ch> * WireColumn.column_type)> ) ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (s) with
| [] -> begin
     []
     end
| ((n, uu___))::t -> begin
     (((n), ((column_json_t h (t_column_or_placeholder n cs)))))::(columns_json_t h t cs)
     end))


let encode_json_t = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( src  :  typed_source<'num, 'flt> ) -> (match (src) with
| TEmbedded (t) -> begin
     WireCanon.JObj ((((WireColumn.schema_key), (WireCanon.JArr ((WireColumn.schema_json_items t.tschema)))))::(((WireColumn.columns_key), (WireCanon.JObj ((columns_json_t h t.tschema t.tcolumns)))))::[])
     end
| TRef (r) -> begin
     WireCanon.JObj ((((WireColumn.schema_key), (WireCanon.JArr ([]))))::(((WireColumn.ref_key), (WireCanon.JStr (r))))::[])
     end))


let try_encode_json_t = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( src  :  typed_source<'num, 'flt> ) -> (match (src) with
| TRef (uu___) -> begin
     WireColumn.Good ((encode_json_t h src))
     end
| TEmbedded (t) -> begin
     (match ((validate_t h t)) with
| WireColumn.Bad (e) -> begin
     WireColumn.Bad (e)
     end
| WireColumn.Good (uu___) -> begin
     WireColumn.Good ((encode_json_t h src))
     end)
     end))


let fits = (fun ( ty  :  WireColumn.column_type ) ( c  :  WireColumn.cell<'num, 'flt> ) -> (match ((WireColumn.type_of c)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| FStar_Pervasives_Native.Some (t) -> begin
     (WireColumn.widens t ty)
     end))


let rec all_fit = (fun ( ty  :  WireColumn.column_type ) ( cs  :  Prims.list<WireColumn.cell<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((fits ty c) && (all_fit ty t))
     end))


let readable_in = (fun ( ty  :  WireColumn.column_type ) ( c  :  WireColumn.cell<'num, 'flt> ) -> (match (((ty), (c))) with
| (WireColumn.DateType, WireColumn.Date (s)) -> begin
     (Temporal.is_canonical_date s)
     end
| (WireColumn.TimestampType (uu___), WireColumn.Timestamp (s)) -> begin
     (Temporal.is_canonical_timestamp s)
     end
| uu___ -> begin
     true
     end))


let rec all_readable = (fun ( ty  :  WireColumn.column_type ) ( cs  :  Prims.list<WireColumn.cell<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((readable_in ty c) && (all_readable ty t))
     end))


let rec zeroed = (fun ( zero  :  'a ) ( xs  :  Prims.list<'a> ) ( v  :  validity ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::xt -> begin
     (match (v) with
| (true)::vt -> begin
     (zeroed zero xt vt)
     end
| (false)::vt -> begin
     ((Prims.op_Equals x zero) && (zeroed zero xt vt))
     end
| [] -> begin
     ((Prims.op_Equals x zero) && (zeroed zero xt []))
     end)
     end))


let rec all_present = (fun ( ok  :  'a  ->  Prims.bool ) ( xs  :  Prims.list<'a> ) ( v  :  validity ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::xt -> begin
     (match (v) with
| (true)::vt -> begin
     ((ok x) && (all_present ok xt vt))
     end
| (false)::vt -> begin
     (all_present ok xt vt)
     end
| [] -> begin
     true
     end)
     end))


let always = (fun ( uu___  :  'a ) -> true)


let instant_in : Temporal.time_unit  ->  (Prims.int * Prims.int)  ->  Prims.bool = (fun ( u  :  Temporal.time_unit ) ( p  :  (Prims.int * Prims.int) ) -> (Temporal.is_instant_in_range u (FStar_Pervasives_Native.fst p) (FStar_Pervasives_Native.snd p)))


let temporal_ok = (fun ( c  :  typed_column<'num, 'flt> ) -> (match (c.col_data) with
| Dates (xs, v) -> begin
     (all_present Temporal.is_day_in_range xs v)
     end
| Timestamps (u, xs, f, v) -> begin
     (all_present (instant_in u) (pairs xs f) v)
     end
| uu___ -> begin
     true
     end))


let zeroed_col = (fun ( h  :  WireColumn.host<'num, 'flt> ) ( c  :  typed_column<'num, 'flt> ) -> (match (c.col_data) with
| Ints (xs, v) -> begin
     (zeroed h.zero_int xs v)
     end
| Floats (xs, v) -> begin
     (zeroed h.zero_float xs v)
     end
| Bools (xs, v) -> begin
     (zeroed false xs v)
     end
| Strs (xs, v) -> begin
     (zeroed [] xs v)
     end
| Dates (xs, v) -> begin
     (zeroed (Prims.parse_int "0") xs v)
     end
| Timestamps (uu___, xs, f, v) -> begin
     (zeroed (((Prims.parse_int "0")), ((Prims.parse_int "0"))) (pairs xs f) v)
     end
| Decimals (xs, v) -> begin
     (zeroed WireColumn.dec_zero xs v)
     end))


let normal_frac = (fun ( c  :  typed_column<'num, 'flt> ) -> (match (c.col_data) with
| Timestamps (u, xs, f, v) -> begin
     ((match (f) with
| FStar_Pervasives_Native.Some (fs) -> begin
     (WireColumn.same_len fs xs)
     end
| FStar_Pervasives_Native.None -> begin
     true
     end) && (Prims.op_Equals f (normal_fraction u (frac_list (units xs) f) v)))
     end
| uu___ -> begin
     true
     end))


let never = (fun ( uu___  :  'a ) -> false)


let frac_in : Temporal.time_unit  ->  (Prims.int * Prims.int)  ->  Prims.bool = (fun ( u  :  Temporal.time_unit ) ( p  :  (Prims.int * Prims.int) ) -> (((Prims.parse_int "0") <= (FStar_Pervasives_Native.snd p)) && ((FStar_Pervasives_Native.snd p) < (Temporal.unit_scale u))))


let frac_ok = (fun ( c  :  typed_column<'num, 'flt> ) -> (match (c.col_data) with
| Timestamps (u, xs, f, v) -> begin
     ((not ((frac_ragged xs f))) && (all_present (frac_in u) (pairs xs f) v))
     end
| uu___ -> begin
     true
     end))


let rec all_frac_ok = (fun ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (cs) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((frac_ok c) && (all_frac_ok t))
     end))


let rec normal_columns_t = (fun ( s  :  Prims.list<(Prims.list<WireCanon.ch> * WireColumn.column_type)> ) ( cs  :  Prims.list<typed_column<'num, 'flt>> ) -> (match (s) with
| [] -> begin
     []
     end
| ((n, uu___))::t -> begin
     ((t_column_or_placeholder n cs))::(normal_columns_t t cs)
     end))


let normal_table_t = (fun ( t  :  typed_table<'num, 'flt> ) -> {tschema = t.tschema; tcolumns = (normal_columns_t t.tschema t.tcolumns)})

type validity_rep =
| AllValid
| Mask of validity


let uu___is_AllValid : validity_rep  ->  Prims.bool = (fun ( projectee  :  validity_rep ) -> (match (projectee) with
| AllValid -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Mask : validity_rep  ->  Prims.bool = (fun ( projectee  :  validity_rep ) -> (match (projectee) with
| Mask (present) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Mask__item__present : validity_rep  ->  validity = (fun ( projectee  :  validity_rep ) -> (match (projectee) with
| Mask (present) -> begin
     present
     end))


let rec all_true : Prims.list<unit>  ->  validity = (fun ( n  :  Prims.list<unit> ) -> (match (n) with
| [] -> begin
     []
     end
| (uu___)::t -> begin
     (true)::(all_true t)
     end))


let to_mask : Prims.list<unit>  ->  validity_rep  ->  validity = (fun ( n  :  Prims.list<unit> ) ( v  :  validity_rep ) -> (match (v) with
| AllValid -> begin
     (all_true n)
     end
| Mask (m) -> begin
     m
     end))


let rec all_set : validity  ->  Prims.bool = (fun ( m  :  validity ) -> (match (m) with
| [] -> begin
     true
     end
| (b)::t -> begin
     (b && (all_set t))
     end))


let of_mask : validity  ->  validity_rep = (fun ( m  :  validity ) ->  
if (all_set m) then begin
     AllValid
     end else begin
     Mask (m)
     end)


let same_mask : Prims.list<unit>  ->  validity_rep  ->  validity_rep  ->  Prims.bool = (fun ( n  :  Prims.list<unit> ) ( a  :  validity_rep ) ( b  :  validity_rep ) -> (match (((a), (b))) with
| (AllValid, AllValid) -> begin
     true
     end
| (AllValid, Mask (m)) -> begin
     ((WireColumn.same_len m n) && (all_set m))
     end
| (Mask (m), AllValid) -> begin
     ((WireColumn.same_len m n) && (all_set m))
     end
| (Mask (x), Mask (y)) -> begin
     (Prims.op_Equals x y)
     end))


let twin_host : WireColumn.host<Prims.nat, Prims.nat> = {WireColumn.to_float = (fun ( i  :  Prims.nat ) -> i); WireColumn.int_text = (fun ( uu___  :  Prims.nat ) -> []); WireColumn.finite = (fun ( uu___  :  Prims.nat ) -> true); WireColumn.zero_int = (Prims.parse_int "0"); WireColumn.zero_float = (Prims.parse_int "0")}

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


let twins : Prims.list<twin> = ({tname = "to-cells-reads-the-mask"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (to_cells {col_name = []; col_data = Ints (((Prims.parse_int "1"))::((Prims.parse_int "2"))::((Prims.parse_int "3"))::[], (true)::(false)::[])}) ((WireColumn.Int ((Prims.parse_int "1")))::(WireColumn.Null)::(WireColumn.Null)::[])))})::({tname = "of-cells-widens-an-int-into-a-float-column"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (of_cells twin_host [] WireColumn.FloatType ((WireColumn.Int ((Prims.parse_int "3")))::(WireColumn.Null)::(WireColumn.Float ((Prims.parse_int "4")))::[])) (WireColumn.Good ({col_name = []; col_data = Floats (((Prims.parse_int "3"))::((Prims.parse_int "0"))::((Prims.parse_int "4"))::[], (true)::(false)::(true)::[])}))))})::({tname = "of-cells-refuses-the-first-cell-outside"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (of_cells twin_host [] WireColumn.BoolType ((WireColumn.Null)::(WireColumn.Int ((Prims.parse_int "1")))::(WireColumn.Bool (true))::[])) (WireColumn.Bad (WireColumn.TypeMismatch ([], WireColumn.BoolType)))))})::({tname = "first-uncarriable-names-the-mask-first"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (first_uncarriable_t twin_host {col_name = []; col_data = Ints (((Prims.parse_int "1"))::[], [])}) (FStar_Pervasives_Native.Some (WireColumn.LengthMismatch ([])))))})::({tname = "column-json-writes-the-absent-slot-not-the-element"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (column_json_t twin_host {col_name = []; col_data = Ints (((Prims.parse_int "7"))::((Prims.parse_int "9"))::[], (false)::(true)::[])}) (WireCanon.JObj ((((WireColumn.values_key), (WireCanon.JArr ((WireCanon.JInt ((Prims.parse_int "0")))::(WireCanon.JInt ((Prims.parse_int "9")))::[]))))::(((WireColumn.validity_key), (WireCanon.JArr ((WireCanon.JBool (false))::(WireCanon.JBool (true))::[]))))::[]))))})::({tname = "of-mask-normalises-an-all-set-mask"; tholds = (fun ( uu___  :  unit ) -> (((Prims.op_Equals (of_mask ((true)::(true)::[])) AllValid) && (Prims.op_Equals (of_mask ((true)::(false)::[])) (Mask ((true)::(false)::[])))) && (Prims.op_Equals (of_mask []) AllValid)))})::({tname = "same-mask-reads-all-valid-as-the-all-true-mask-of-the-length"; tholds = (fun ( uu___  :  unit ) -> ((((same_mask ((())::(())::[]) AllValid (Mask ((true)::(true)::[]))) && (not ((same_mask ((())::(())::[]) AllValid (Mask ((true)::(true)::(true)::[])))))) && (not ((same_mask ((())::(())::[]) (Mask ((true)::(false)::[])) AllValid)))) && (Prims.op_Equals (to_mask ((())::(())::[]) AllValid) ((true)::(true)::[]))))})::({tname = "temporal-columns-render-their-text-and-read-it-back"; tholds = (fun ( uu___  :  unit ) -> (

let d = {col_name = []; col_data = Dates (((Prims.parse_int "0"))::((Prims.parse_int "20512"))::[], (true)::(true)::[])}
in (

let t = {col_name = []; col_data = Timestamps (Temporal.Milliseconds, ((Prims.parse_int "45296"))::((Prims.parse_int "0"))::[], FStar_Pervasives_Native.Some (((Prims.parse_int "500"))::((Prims.parse_int "0"))::[]), (true)::(false)::[])}
in (((((((Prims.op_Equals (to_cells d) ((WireColumn.Date ((Temporal.date_text (Prims.parse_int "0"))))::(WireColumn.Date ((Temporal.date_text (Prims.parse_int "20512"))))::[])) && (Prims.op_Equals (of_cells twin_host [] WireColumn.DateType (to_cells d)) (WireColumn.Good (d)))) && (Prims.op_Equals (to_cells t) ((WireColumn.Timestamp ((Temporal.instant_text Temporal.Milliseconds (Prims.parse_int "45296") (Prims.parse_int "500"))))::(WireColumn.Null)::[]))) && (Prims.op_Equals (of_cells twin_host [] (WireColumn.TimestampType (Temporal.Milliseconds)) (to_cells t)) (WireColumn.Good (t)))) && (Prims.op_Equals (of_cells twin_host [] (WireColumn.TimestampType (Temporal.Seconds)) (to_cells t)) (WireColumn.Bad (WireColumn.TypeMismatch ([], WireColumn.TimestampType (Temporal.Seconds)))))) && (Prims.op_Equals (of_cells twin_host [] (WireColumn.TimestampType (Temporal.Milliseconds)) ((WireColumn.Timestamp ((WireCanon.CPlain ("x"))::[]))::[])) (WireColumn.Bad (WireColumn.MalformedShape)))) && (Prims.op_Equals (first_uncarriable_t twin_host {col_name = []; col_data = Timestamps (Temporal.Milliseconds, ((Prims.parse_int "0"))::[], FStar_Pervasives_Native.Some (((Prims.parse_int "1000"))::[]), (true)::[])}) (FStar_Pervasives_Native.Some (WireColumn.MalformedShape)))))))})::({tname = "a-dropped-fraction-reads-as-zeros"; tholds = (fun ( uu___  :  unit ) -> ((((data_eq (Timestamps (Temporal.Milliseconds, ((Prims.parse_int "7"))::[], FStar_Pervasives_Native.None, (true)::[])) (Timestamps (Temporal.Milliseconds, ((Prims.parse_int "7"))::[], FStar_Pervasives_Native.Some (((Prims.parse_int "0"))::[]), (true)::[]))) && (not ((data_eq (Timestamps (Temporal.Milliseconds, ((Prims.parse_int "7"))::[], FStar_Pervasives_Native.None, (true)::[])) (Timestamps (Temporal.Milliseconds, ((Prims.parse_int "7"))::[], FStar_Pervasives_Native.Some (((Prims.parse_int "1"))::[]), (true)::[])))))) && (Prims.op_Equals (normal_fraction Temporal.Milliseconds (((Prims.parse_int "0"))::((Prims.parse_int "5"))::[]) ((true)::(false)::[])) FStar_Pervasives_Native.None)) && (Prims.op_Equals (normal_fraction Temporal.Milliseconds (((Prims.parse_int "0"))::((Prims.parse_int "5"))::[]) ((true)::(true)::[])) (FStar_Pervasives_Native.Some (((Prims.parse_int "0"))::((Prims.parse_int "5"))::[])))))})::({tname = "data-eq-ignores-an-absent-element"; tholds = (fun ( uu___  :  unit ) -> ((data_eq (Ints (((Prims.parse_int "7"))::((Prims.parse_int "9"))::[], (false)::(true)::[])) (Ints (((Prims.parse_int "0"))::((Prims.parse_int "9"))::[], (false)::(true)::[]))) && (not ((data_eq (Ints (((Prims.parse_int "7"))::((Prims.parse_int "9"))::[], (true)::(true)::[])) (Ints (((Prims.parse_int "0"))::((Prims.parse_int "9"))::[], (true)::(true)::[])))))))})::[]




