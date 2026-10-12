module DecodeLayer
type seg =
| Key of Prims.string
| Index of Prims.int


let uu___is_Key : seg  ->  Prims.bool = (fun ( projectee  :  seg ) -> (match (projectee) with
| Key (k) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Key__item__k : seg  ->  Prims.string = (fun ( projectee  :  seg ) -> (match (projectee) with
| Key (k) -> begin
     k
     end))


let uu___is_Index : seg  ->  Prims.bool = (fun ( projectee  :  seg ) -> (match (projectee) with
| Index (i) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Index__item__i : seg  ->  Prims.int = (fun ( projectee  :  seg ) -> (match (projectee) with
| Index (i) -> begin
     i
     end))

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


let uu___is_InvalidJson : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| InvalidJson -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_MissingField : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| MissingField -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_WrongKind : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| WrongKind -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UnknownTag : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| UnknownTag -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OutOfRange : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| OutOfRange -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UndeclaredMember : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| UndeclaredMember -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_LimitExceeded : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| LimitExceeded -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_NotAdmitted : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| NotAdmitted -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SchemaFault : code  ->  Prims.bool = (fun ( projectee  :  code ) -> (match (projectee) with
| SchemaFault -> begin
     true
     end
| uu___ -> begin
     false
     end))

type derr = {code : code; path : Prims.list<seg>}


let __proj__Mkderr__item__code : derr  ->  code = (fun ( projectee  :  derr ) -> (match (projectee) with
| {code = code1; path = path} -> begin
     code1
     end))


let __proj__Mkderr__item__path : derr  ->  Prims.list<seg> = (fun ( projectee  :  derr ) -> (match (projectee) with
| {code = code1; path = path} -> begin
     path
     end))

type result<'a, 'e> =
| Ok of 'a
| Error of 'e


let uu___is_Ok = (fun ( projectee  :  result<'a, 'e> ) -> (match (projectee) with
| Ok (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ok__item__v = (fun ( projectee  :  result<'a, 'e> ) -> (match (projectee) with
| Ok (v) -> begin
     v
     end))


let uu___is_Error = (fun ( projectee  :  result<'a, 'e> ) -> (match (projectee) with
| Error (err) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Error__item__err = (fun ( projectee  :  result<'a, 'e> ) -> (match (projectee) with
| Error (err) -> begin
     err
     end))


let rec app = (fun ( l1  :  Prims.list<'a> ) ( l2  :  Prims.list<'a> ) -> (match (l1) with
| [] -> begin
     l2
     end
| (x)::t -> begin
     (x)::(app t l2)
     end))


let rec mem = (fun ( x  :  'a ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals y x) || (mem x t))
     end))


let rec map_l = (fun ( f  :  'a  ->  'b ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((f x))::(map_l f t)
     end))


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let rec filter_l = (fun ( p  :  'a  ->  Prims.bool ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (p x) then begin
     (x)::(filter_l p t)
     end else begin
     (filter_l p t)
     end
     end))


let rec concat_map = (fun ( f  :  'a  ->  Prims.list<'b> ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (app (f x) (concat_map f t))
     end))


let rec keys = (fun ( l  :  Prims.list<(Prims.string * 'a)> ) -> (match (l) with
| [] -> begin
     []
     end
| ((k, uu___))::t -> begin
     (k)::(keys t)
     end))


let map_err = (fun ( f  :  'e1  ->  'e2 ) ( r  :  result<'a, 'e1> ) -> (match (r) with
| Ok (v) -> begin
     Ok (v)
     end
| Error (e) -> begin
     Error ((f e))
     end))


let make : code  ->  derr = (fun ( c  :  code ) -> {code = c; path = []})


let under : seg  ->  derr  ->  derr = (fun ( s  :  seg ) ( e  :  derr ) -> {code = e.code; path = (s)::e.path})


let within : Prims.list<seg>  ->  derr  ->  derr = (fun ( p  :  Prims.list<seg> ) ( e  :  derr ) -> {code = e.code; path = (app p e.path)})


let rec memb = (fun ( k  :  Prims.string ) ( fs  :  Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, v))::t -> begin
      
if (Prims.op_Equals n k) then begin
     FStar_Pervasives_Native.Some (v)
     end else begin
     (memb k t)
     end
     end))


let rec try_item = (fun ( i  :  Prims.int ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     FStar_Pervasives_Native.Some (x)
     end else begin
     (try_item (i - (Prims.parse_int "1")) t)
     end
     end))


let step = (fun ( s  :  seg ) ( v  :  WireDecode.jval<'num, 'flt> ) -> (match (((s), (v))) with
| (Key (k), WireDecode.JObj (fs)) -> begin
     (memb k fs)
     end
| (Index (i), WireDecode.JArr (xs)) -> begin
      
if (i >= (Prims.parse_int "0")) then begin
     (try_item i xs)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end))


let rec resolve = (fun ( p  :  Prims.list<seg> ) ( doc  :  WireDecode.jval<'num, 'flt> ) -> (match (p) with
| [] -> begin
     FStar_Pervasives_Native.Some (doc)
     end
| (s)::rest -> begin
     (match ((step s doc)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (resolve rest v)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end))


let rec has_key = (fun ( k  :  Prims.string ) ( fs  :  Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     false
     end
| ((n, uu___))::t -> begin
     ((Prims.op_Equals n k) || (has_key k t))
     end))


let resolves_in = (fun ( doc  :  WireDecode.jval<'num, 'flt> ) ( e  :  derr ) -> (match (e.code) with
| InvalidJson -> begin
     (match (e.path) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| MissingField -> begin
     (match ((WireDecode.rev e.path)) with
| (Key (k))::parent_rev -> begin
     (match ((resolve (WireDecode.rev parent_rev) doc)) with
| FStar_Pervasives_Native.Some (WireDecode.JObj (fields)) -> begin
     (not ((has_key k fields)))
     end
| uu___ -> begin
     false
     end)
     end
| uu___ -> begin
     false
     end)
     end
| uu___ -> begin
     (match ((resolve e.path doc)) with
| FStar_Pervasives_Native.Some (uu___1) -> begin
     true
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)
     end))


type dec<'num, 'flt, 'a> = WireDecode.jval<'num, 'flt>  ->  result<'a, derr>


let succeed = (fun ( v  :  'a ) ( uu___  :  WireDecode.jval<'num, 'flt> ) -> Ok (v))


let fail = (fun ( c  :  code ) ( uu___  :  WireDecode.jval<'num, 'flt> ) -> Error ((make c)))


let map = (fun ( g  :  'a  ->  'b ) ( d  :  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match ((d el)) with
| Ok (v) -> begin
     Ok ((g v))
     end
| Error (e) -> begin
     Error (e)
     end))


let bind = (fun ( g  :  'a  ->  dec<'num, 'flt, 'b> ) ( d  :  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match ((d el)) with
| Ok (v) -> begin
     (g v el)
     end
| Error (e) -> begin
     Error (e)
     end))


let and_then = (fun ( g  :  'a  ->  result<'b, derr> ) ( d  :  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match ((d el)) with
| Ok (v) -> begin
     (g v)
     end
| Error (e) -> begin
     Error (e)
     end))


let wrong_kind = (fun ( found  :  WireDecode.jval<'num, 'flt> ) -> (make WrongKind))


let missing : Prims.string  ->  derr = (fun ( name  :  Prims.string ) -> {code = MissingField; path = (Key (name))::[]})


let try_member = (fun ( name  :  Prims.string ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JObj (fields) -> begin
     (memb name fields)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let json = (fun ( el  :  WireDecode.jval<'num, 'flt> ) -> Ok (el))


let str = (fun ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JStr (s) -> begin
     Ok (s)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let int_d = (fun ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JInt (i) -> begin
     Ok (i)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let float_d = (fun ( to_flt  :  'num  ->  'flt ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JFloat (f) -> begin
     Ok (f)
     end
| WireDecode.JInt (i) -> begin
     Ok ((to_flt i))
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let bool_d = (fun ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JBool (b) -> begin
     Ok (b)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let items = (fun ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JArr (xs) -> begin
     Ok (xs)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let obj = (fun ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JObj (fields) -> begin
     Ok (fields)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let field = (fun ( name  :  Prims.string ) ( d  :  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JObj (uu___) -> begin
     (match ((try_member name el)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (map_err (under (Key (name))) (d v))
     end
| FStar_Pervasives_Native.None -> begin
     Error ((missing name))
     end)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let opt_field = (fun ( name  :  Prims.string ) ( d  :  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JObj (uu___) -> begin
     (match ((try_member name el)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (match ((d v)) with
| Ok (x) -> begin
     Ok (FStar_Pervasives_Native.Some (x))
     end
| Error (e) -> begin
     Error ((under (Key (name)) e))
     end)
     end
| FStar_Pervasives_Native.None -> begin
     Ok (FStar_Pervasives_Native.None)
     end)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let field_or = (fun ( name  :  Prims.string ) ( fallback  :  'a ) ( d  :  dec<'num, 'flt, 'a> ) -> (map (fun ( o  :  FStar_Pervasives_Native.option<'a> ) -> (match (o) with
| FStar_Pervasives_Native.Some (x) -> begin
     x
     end
| FStar_Pervasives_Native.None -> begin
     fallback
     end)) (opt_field name d)))


let rec at_go = (fun ( d  :  dec<'num, 'flt, 'a> ) ( done_rev  :  Prims.list<seg> ) ( rest  :  Prims.list<seg> ) ( v  :  WireDecode.jval<'num, 'flt> ) -> (

let here = (WireDecode.rev done_rev)
in (match (rest) with
| [] -> begin
     (map_err (within here) (d v))
     end
| (Key (k))::tail -> begin
     (match (v) with
| WireDecode.JObj (uu___) -> begin
     (match ((try_member k v)) with
| FStar_Pervasives_Native.Some (x) -> begin
     (at_go d ((Key (k))::done_rev) tail x)
     end
| FStar_Pervasives_Native.None -> begin
     Error ((within here (missing k)))
     end)
     end
| other -> begin
     Error ((within here (wrong_kind other)))
     end)
     end
| (Index (i))::tail -> begin
     (match (v) with
| WireDecode.JArr (xs) -> begin
     (match ( 
if (i >= (Prims.parse_int "0")) then begin
     (try_item i xs)
     end else begin
     FStar_Pervasives_Native.None
     end) with
| FStar_Pervasives_Native.Some (x) -> begin
     (at_go d ((Index (i))::done_rev) tail x)
     end
| FStar_Pervasives_Native.None -> begin
     Error ((within here (make OutOfRange)))
     end)
     end
| other -> begin
     Error ((within here (wrong_kind other)))
     end)
     end)))


let at = (fun ( path  :  Prims.list<seg> ) ( d  :  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (at_go d [] path el))


let rec mli_go = (fun ( g  :  Prims.int  ->  dec<'num, 'flt, 'a> ) ( i  :  Prims.int ) ( acc  :  Prims.list<'a> ) ( xs  :  Prims.list<WireDecode.jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     Ok ((WireDecode.rev acc))
     end
| (x)::rest -> begin
     (match ((g i x)) with
| Ok (v) -> begin
     (mli_go g (i + (Prims.parse_int "1")) ((v)::acc) rest)
     end
| Error (e) -> begin
     Error ((under (Index (i)) e))
     end)
     end))


let map_list_indexed = (fun ( g  :  Prims.int  ->  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JArr (xs) -> begin
     (mli_go g (Prims.parse_int "0") [] xs)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let list_d = (fun ( d  :  dec<'num, 'flt, 'a> ) -> (map_list_indexed (fun ( uu___  :  Prims.int ) -> d)))


let bounded_list = (fun ( max_items  :  Prims.int ) ( d  :  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JArr (xs) -> begin
      
if ((len xs) > max_items) then begin
     Error ((make LimitExceeded))
     end else begin
     (list_d d el)
     end
     end
| uu___ -> begin
     (list_d d el)
     end))


let rec errors_of = (fun ( rs  :  Prims.list<result<'a, derr>> ) -> (match (rs) with
| [] -> begin
     []
     end
| (Error (e))::t -> begin
     (e)::(errors_of t)
     end
| (Ok (uu___))::t -> begin
     (errors_of t)
     end))


let rec oks_of = (fun ( rs  :  Prims.list<result<'a, 'e>> ) -> (match (rs) with
| [] -> begin
     []
     end
| (Ok (v))::t -> begin
     (v)::(oks_of t)
     end
| (Error (uu___))::t -> begin
     (oks_of t)
     end))


let rec mapi_d = (fun ( d  :  dec<'num, 'flt, 'a> ) ( i  :  Prims.int ) ( xs  :  Prims.list<WireDecode.jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((map_err (under (Index (i))) (d x)))::(mapi_d d (i + (Prims.parse_int "1")) t)
     end))


let list_all = (fun ( d  :  dec<'num, 'flt, 'a> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JArr (xs) -> begin
     (

let rs = (mapi_d d (Prims.parse_int "0") xs)
in (match ((errors_of rs)) with
| [] -> begin
     Ok ((oks_of rs))
     end
| errs -> begin
     Error (errs)
     end))
     end
| other -> begin
     Error (((wrong_kind other))::[])
     end))


let rec seq_go = (fun ( el  :  WireDecode.jval<'num, 'flt> ) ( acc  :  Prims.list<'a> ) ( ds  :  Prims.list<dec<'num, 'flt, 'a>> ) -> (match (ds) with
| [] -> begin
     Ok ((WireDecode.rev acc))
     end
| (d)::rest -> begin
     (match ((d el)) with
| Ok (v) -> begin
     (seq_go el ((v)::acc) rest)
     end
| Error (e) -> begin
     Error (e)
     end)
     end))


let sequence = (fun ( ds  :  Prims.list<dec<'num, 'flt, 'a>> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (seq_go el [] ds))


let rec run_all = (fun ( ds  :  Prims.list<dec<'num, 'flt, 'a>> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (ds) with
| [] -> begin
     []
     end
| (d)::t -> begin
     ((d el))::(run_all t el)
     end))


let all_d = (fun ( ds  :  Prims.list<dec<'num, 'flt, 'a>> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (

let rs = (run_all ds el)
in (match ((errors_of rs)) with
| [] -> begin
     Ok ((oks_of rs))
     end
| errs -> begin
     Error (errs)
     end)))


let rec find_case = (fun ( s  :  Prims.string ) ( cases  :  Prims.list<(Prims.string * 'a)> ) -> (match (cases) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((k, v))::t -> begin
      
if (Prims.op_Equals k s) then begin
     FStar_Pervasives_Native.Some (v)
     end else begin
     (find_case s t)
     end
     end))


let one_of = (fun ( cases  :  Prims.list<(Prims.string * 'a)> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JStr (s) -> begin
     (match ((find_case s cases)) with
| FStar_Pervasives_Native.Some (v) -> begin
     Ok (v)
     end
| FStar_Pervasives_Native.None -> begin
     Error ((make UnknownTag))
     end)
     end
| other -> begin
     Error ((wrong_kind other))
     end))


let tag_dispatch = (fun ( key  :  Prims.string ) ( cases  :  Prims.list<(Prims.string * dec<'num, 'flt, 'a>)> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match ((field key str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (t) -> begin
     (match ((find_case t cases)) with
| FStar_Pervasives_Native.Some (d) -> begin
     (d el)
     end
| FStar_Pervasives_Native.None -> begin
     Error ((under (Key (key)) (make UnknownTag)))
     end)
     end))


let tag_dispatch_with = (fun ( what  :  Prims.string ) ( key  :  Prims.string ) ( cases  :  Prims.list<(Prims.string * dec<'num, 'flt, 'a>)> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (tag_dispatch key cases el))


let kind_dispatch = (fun ( cases  :  Prims.list<(Prims.string * dec<'num, 'flt, 'a>)> ) -> (tag_dispatch "kind" cases))


let int_range = (fun ( in_range  :  'num  ->  Prims.bool ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match ((int_d el)) with
| Ok (i) -> begin
      
if (in_range i) then begin
     Ok (i)
     end else begin
     Error ((make OutOfRange))
     end
     end
| Error (e) -> begin
     Error (e)
     end))


let rec undeclared_go = (fun ( known  :  Prims.list<Prims.string> ) ( fields  :  Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)> ) -> (match (fields) with
| [] -> begin
     []
     end
| ((k, uu___))::t -> begin
      
if (mem k known) then begin
     (undeclared_go known t)
     end else begin
     ((under (Key (k)) (make UndeclaredMember)))::(undeclared_go known t)
     end
     end))


let undeclared = (fun ( known  :  Prims.list<Prims.string> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match (el) with
| WireDecode.JObj (fields) -> begin
     (undeclared_go known fields)
     end
| uu___ -> begin
     []
     end))


let members = (fun ( known  :  Prims.list<Prims.string> ) ( el  :  WireDecode.jval<'num, 'flt> ) -> (match ((undeclared known el)) with
| [] -> begin
     Ok (())
     end
| (e)::uu___ -> begin
     Error (e)
     end))


let closed = (fun ( known  :  Prims.list<Prims.string> ) ( d  :  dec<'num, 'flt, 'a> ) -> (bind (fun ( uu___  :  unit ) -> d) (members known)))

type fault =
| RepeatedSpelling of Prims.string
| RepeatedMember of Prims.string * FStar_Pervasives_Native.option<Prims.string>
| RepeatedTag of Prims.string
| DiscriminatorAsMember of Prims.string * Prims.string
| Unrecognised of Prims.list<seg>


let uu___is_RepeatedSpelling : fault  ->  Prims.bool = (fun ( projectee  :  fault ) -> (match (projectee) with
| RepeatedSpelling (spelling) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RepeatedSpelling__item__spelling : fault  ->  Prims.string = (fun ( projectee  :  fault ) -> (match (projectee) with
| RepeatedSpelling (spelling) -> begin
     spelling
     end))


let uu___is_RepeatedMember : fault  ->  Prims.bool = (fun ( projectee  :  fault ) -> (match (projectee) with
| RepeatedMember (member_name, in_case) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RepeatedMember__item__member_name : fault  ->  Prims.string = (fun ( projectee  :  fault ) -> (match (projectee) with
| RepeatedMember (member_name, in_case) -> begin
     member_name
     end))


let __proj__RepeatedMember__item__in_case : fault  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( projectee  :  fault ) -> (match (projectee) with
| RepeatedMember (member_name, in_case) -> begin
     in_case
     end))


let uu___is_RepeatedTag : fault  ->  Prims.bool = (fun ( projectee  :  fault ) -> (match (projectee) with
| RepeatedTag (repeated_tag) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RepeatedTag__item__repeated_tag : fault  ->  Prims.string = (fun ( projectee  :  fault ) -> (match (projectee) with
| RepeatedTag (repeated_tag) -> begin
     repeated_tag
     end))


let uu___is_DiscriminatorAsMember : fault  ->  Prims.bool = (fun ( projectee  :  fault ) -> (match (projectee) with
| DiscriminatorAsMember (case_tag, discriminator) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DiscriminatorAsMember__item__case_tag : fault  ->  Prims.string = (fun ( projectee  :  fault ) -> (match (projectee) with
| DiscriminatorAsMember (case_tag, discriminator) -> begin
     case_tag
     end))


let __proj__DiscriminatorAsMember__item__discriminator : fault  ->  Prims.string = (fun ( projectee  :  fault ) -> (match (projectee) with
| DiscriminatorAsMember (case_tag, discriminator) -> begin
     discriminator
     end))


let uu___is_Unrecognised : fault  ->  Prims.bool = (fun ( projectee  :  fault ) -> (match (projectee) with
| Unrecognised (at_path) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Unrecognised__item__at_path : fault  ->  Prims.list<seg> = (fun ( projectee  :  fault ) -> (match (projectee) with
| Unrecognised (at_path) -> begin
     at_path
     end))


let fault_under : seg  ->  fault  ->  fault = (fun ( s  :  seg ) ( f  :  fault ) -> (match (f) with
| Unrecognised (p) -> begin
     Unrecognised ((s)::p)
     end
| other -> begin
     other
     end))

type codec<'num, 'flt, 'a> = {write : 'a  ->  result<WireDecode.jval<'num, 'flt>, fault>; read_all : WireDecode.jval<'num, 'flt>  ->  result<'a, Prims.list<derr>>}


let __proj__Mkcodec__item__write = (fun ( projectee  :  codec<'num, 'flt, 'a> ) -> (match (projectee) with
| {write = write; read_all = read_all} -> begin
     write
     end))


let __proj__Mkcodec__item__read_all = (fun ( projectee  :  codec<'num, 'flt, 'a> ) -> (match (projectee) with
| {write = write; read_all = read_all} -> begin
     read_all
     end))


let one = (fun ( r  :  result<'a, derr> ) -> (match (r) with
| Ok (v) -> begin
     Ok (v)
     end
| Error (e) -> begin
     Error ((e)::[])
     end))


let under_all = (fun ( s  :  seg ) ( r  :  result<'a, Prims.list<derr>> ) -> (match (r) with
| Ok (v) -> begin
     Ok (v)
     end
| Error (es) -> begin
     Error ((map_l (under s) es))
     end))


let both = (fun ( fr  :  result<'a  ->  'b, Prims.list<derr>> ) ( ar  :  result<'a, Prims.list<derr>> ) -> (match (((fr), (ar))) with
| (Ok (g), Ok (x)) -> begin
     Ok ((g x))
     end
| (Error (e), Ok (uu___)) -> begin
     Error (e)
     end
| (Ok (uu___), Error (e)) -> begin
     Error (e)
     end
| (Error (e1), Error (e2)) -> begin
     Error ((app e1 e2))
     end))


let c_make = (fun ( w  :  'a  ->  WireDecode.jval<'num, 'flt> ) ( r  :  WireDecode.jval<'num, 'flt>  ->  result<'a, Prims.list<derr>> ) -> {write = (fun ( v  :  'a ) -> Ok ((w v))); read_all = r})


let c_of_decoder = (fun ( w  :  'a  ->  WireDecode.jval<'num, 'flt> ) ( d  :  dec<'num, 'flt, 'a> ) -> (c_make w (fun ( j  :  WireDecode.jval<'num, 'flt> ) -> (one (d j)))))


let c_decoder = (fun ( c  :  codec<'num, 'flt, 'a> ) ( j  :  WireDecode.jval<'num, 'flt> ) -> (match ((c.read_all j)) with
| Ok (v) -> begin
     Ok (v)
     end
| Error ((e)::uu___) -> begin
     Error (e)
     end
| Error ([]) -> begin
     Error ((make SchemaFault))
     end))


let c_json = (fun ( uu___  :  unit ) -> (c_make (fun ( j  :  WireDecode.jval<'num, 'flt> ) -> j) (fun ( j  :  WireDecode.jval<'num, 'flt> ) -> Ok (j))))


let c_string = (fun ( uu___  :  unit ) -> (c_of_decoder (fun ( s  :  Prims.string ) -> WireDecode.JStr (s)) str))


let c_int = (fun ( uu___  :  unit ) -> (c_of_decoder (fun ( i  :  'num ) -> WireDecode.JInt (i)) int_d))


let c_float = (fun ( to_flt  :  'num  ->  'flt ) -> (c_of_decoder (fun ( f  :  'flt ) -> WireDecode.JFloat (f)) (float_d to_flt)))


let c_bool = (fun ( uu___  :  unit ) -> (c_of_decoder (fun ( b  :  Prims.bool ) -> WireDecode.JBool (b)) bool_d))


let rec occurs_twice : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( x  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
      
if (Prims.op_Equals y x) then begin
     (mem x t)
     end else begin
     (occurs_twice x t)
     end
     end))


let rec dedup_go : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( seen  :  Prims.list<Prims.string> ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (mem x seen) then begin
     (dedup_go seen t)
     end else begin
     (x)::(dedup_go ((x)::seen) t)
     end
     end))


let repeats : Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( names  :  Prims.list<Prims.string> ) -> (filter_l (fun ( x  :  Prims.string ) -> (occurs_twice x names)) (dedup_go [] names)))


let rec find_spelling = (fun ( v  :  'a ) ( cases  :  Prims.list<(Prims.string * 'a)> ) -> (match (cases) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((s, x))::t -> begin
      
if (Prims.op_Equals x v) then begin
     FStar_Pervasives_Native.Some (s)
     end else begin
     (find_spelling v t)
     end
     end))


let c_enum = (fun ( cases  :  Prims.list<(Prims.string * 'a)> ) -> (

let repeated = (repeats (keys cases))
in (match (repeated) with
| [] -> begin
     Ok ({write = (fun ( v  :  'a ) -> (match ((find_spelling v cases)) with
| FStar_Pervasives_Native.Some (s) -> begin
     Ok (WireDecode.JStr (s))
     end
| FStar_Pervasives_Native.None -> begin
     Error (Unrecognised ([]))
     end)); read_all = (fun ( j  :  WireDecode.jval<'num, 'flt> ) -> (one (one_of cases j)))})
     end
| uu___ -> begin
     Error ((map_l (fun ( s  :  Prims.string ) -> RepeatedSpelling (s)) repeated))
     end)))


let rec traverse_go = (fun ( w  :  Prims.int  ->  'a  ->  result<'b, fault> ) ( i  :  Prims.int ) ( acc  :  Prims.list<'b> ) ( xs  :  Prims.list<'a> ) -> (match (xs) with
| [] -> begin
     Ok ((WireDecode.rev acc))
     end
| (x)::rest -> begin
     (match ((w i x)) with
| Ok (y) -> begin
     (traverse_go w (i + (Prims.parse_int "1")) ((y)::acc) rest)
     end
| Error (f) -> begin
     Error (f)
     end)
     end))


let traverse = (fun ( w  :  Prims.int  ->  'a  ->  result<'b, fault> ) ( xs  :  Prims.list<'a> ) -> (traverse_go w (Prims.parse_int "0") [] xs))


let rec mapi_all = (fun ( r  :  WireDecode.jval<'num, 'flt>  ->  result<'a, Prims.list<derr>> ) ( i  :  Prims.int ) ( xs  :  Prims.list<WireDecode.jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((under_all (Index (i)) (r x)))::(mapi_all r (i + (Prims.parse_int "1")) t)
     end))


let rec collect_errors = (fun ( rs  :  Prims.list<result<'a, Prims.list<derr>>> ) -> (match (rs) with
| [] -> begin
     []
     end
| (Ok (uu___))::t -> begin
     (collect_errors t)
     end
| (Error (es))::t -> begin
     (app es (collect_errors t))
     end))


let item_write = (fun ( c  :  codec<'num, 'flt, 'a> ) ( i  :  Prims.int ) ( x  :  'a ) -> (map_err (fault_under (Index (i))) (c.write x)))


let c_list = (fun ( c  :  codec<'num, 'flt, 'a> ) -> {write = (fun ( xs  :  Prims.list<'a> ) -> (match ((traverse (item_write c) xs)) with
| Ok (js) -> begin
     Ok (WireDecode.JArr (js))
     end
| Error (f) -> begin
     Error (f)
     end)); read_all = (fun ( j  :  WireDecode.jval<'num, 'flt> ) -> (match (j) with
| WireDecode.JArr (xs) -> begin
     (

let rs = (mapi_all c.read_all (Prims.parse_int "0") xs)
in (

let errors = (collect_errors rs)
in (match (errors) with
| [] -> begin
     Ok ((oks_of rs))
     end
| uu___ -> begin
     Error (errors)
     end)))
     end
| other -> begin
     Error (((wrong_kind other))::[])
     end))})


let c_map = (fun ( there  :  'a  ->  'b ) ( back  :  'b  ->  'a ) ( c  :  codec<'num, 'flt, 'a> ) -> {write = (fun ( u  :  'b ) -> (c.write (back u))); read_all = (fun ( j  :  WireDecode.jval<'num, 'flt> ) -> (match ((c.read_all j)) with
| Ok (v) -> begin
     Ok ((there v))
     end
| Error (es) -> begin
     Error (es)
     end))})


let c_refine = (fun ( expected  :  Prims.string ) ( check  :  'a  ->  result<'b, Prims.string> ) ( back  :  'b  ->  'a ) ( c  :  codec<'num, 'flt, 'a> ) -> {write = (fun ( u  :  'b ) -> (c.write (back u))); read_all = (fun ( j  :  WireDecode.jval<'num, 'flt> ) -> (match ((c.read_all j)) with
| Ok (v) -> begin
     (match ((check v)) with
| Ok (u) -> begin
     Ok (u)
     end
| Error (uu___) -> begin
     Error (((make OutOfRange))::[])
     end)
     end
| Error (es) -> begin
     Error (es)
     end))})

type fields<'num, 'flt, 'r, 'c> = {write_fields : 'r  ->  result<Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)>, fault>; read_fields : Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)>  ->  result<'c, Prims.list<derr>>; names : Prims.list<Prims.string>}


let __proj__Mkfields__item__write_fields = (fun ( projectee  :  fields<'num, 'flt, 'r, 'c> ) -> (match (projectee) with
| {write_fields = write_fields; read_fields = read_fields; names = names} -> begin
     write_fields
     end))


let __proj__Mkfields__item__read_fields = (fun ( projectee  :  fields<'num, 'flt, 'r, 'c> ) -> (match (projectee) with
| {write_fields = write_fields; read_fields = read_fields; names = names} -> begin
     read_fields
     end))


let __proj__Mkfields__item__names = (fun ( projectee  :  fields<'num, 'flt, 'r, 'c> ) -> (match (projectee) with
| {write_fields = write_fields; read_fields = read_fields; names = names} -> begin
     names
     end))


let c_record = (fun ( ctor  :  'c ) -> {write_fields = (fun ( uu___  :  'r ) -> Ok ([])); read_fields = (fun ( uu___  :  Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)> ) -> Ok (ctor)); names = []})


let c_field = (fun ( name  :  Prims.string ) ( get  :  'r  ->  'a ) ( cd  :  codec<'num, 'flt, 'a> ) ( fs  :  fields<'num, 'flt, 'r, 'a  ->  'c> ) -> {write_fields = (fun ( x  :  'r ) -> (match ((fs.write_fields x)) with
| Ok (before) -> begin
     (match ((cd.write (get x))) with
| Ok (j) -> begin
     Ok ((app before ((((name), (j)))::[])))
     end
| Error (f) -> begin
     Error ((fault_under (Key (name)) f))
     end)
     end
| Error (f) -> begin
     Error (f)
     end)); read_fields = (fun ( ms  :  Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)> ) -> (

let this = (match ((memb name ms)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (under_all (Key (name)) (cd.read_all v))
     end
| FStar_Pervasives_Native.None -> begin
     Error (((missing name))::[])
     end)
in (both (fs.read_fields ms) this))); names = (app fs.names ((name)::[]))})


let c_opt_field = (fun ( name  :  Prims.string ) ( get  :  'r  ->  FStar_Pervasives_Native.option<'a> ) ( cd  :  codec<'num, 'flt, 'a> ) ( fs  :  fields<'num, 'flt, 'r, FStar_Pervasives_Native.option<'a>  ->  'c> ) -> {write_fields = (fun ( x  :  'r ) -> (match ((get x)) with
| FStar_Pervasives_Native.Some (y) -> begin
     (match ((fs.write_fields x)) with
| Ok (before) -> begin
     (match ((cd.write y)) with
| Ok (j) -> begin
     Ok ((app before ((((name), (j)))::[])))
     end
| Error (f) -> begin
     Error ((fault_under (Key (name)) f))
     end)
     end
| Error (f) -> begin
     Error (f)
     end)
     end
| FStar_Pervasives_Native.None -> begin
     (fs.write_fields x)
     end)); read_fields = (fun ( ms  :  Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)> ) -> (

let this = (match ((memb name ms)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (match ((under_all (Key (name)) (cd.read_all v))) with
| Ok (y) -> begin
     Ok (FStar_Pervasives_Native.Some (y))
     end
| Error (es) -> begin
     Error (es)
     end)
     end
| FStar_Pervasives_Native.None -> begin
     Ok (FStar_Pervasives_Native.None)
     end)
in (both (fs.read_fields ms) this))); names = (app fs.names ((name)::[]))})


let strict_object = (fun ( names  :  Prims.list<Prims.string> ) ( j  :  WireDecode.jval<'num, 'flt> ) ( read  :  Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)>  ->  result<'a, Prims.list<derr>> ) -> (match (j) with
| WireDecode.JObj (ms) -> begin
     (match ((((undeclared names j)), ((read ms)))) with
| ([], r) -> begin
     r
     end
| (u, Ok (uu___)) -> begin
     Error (u)
     end
| (u, Error (e)) -> begin
     Error ((app u e))
     end)
     end
| other -> begin
     Error (((wrong_kind other))::[])
     end))


let c_build = (fun ( fs  :  fields<'num, 'flt, 'r, 'r> ) -> (

let repeated = (repeats fs.names)
in (match (repeated) with
| [] -> begin
     Ok ({write = (fun ( x  :  'r ) -> (match ((fs.write_fields x)) with
| Ok (ws) -> begin
     Ok (WireDecode.JObj (ws))
     end
| Error (f) -> begin
     Error (f)
     end)); read_all = (fun ( j  :  WireDecode.jval<'num, 'flt> ) -> (strict_object fs.names j fs.read_fields))})
     end
| uu___ -> begin
     Error ((map_l (fun ( n  :  Prims.string ) -> RepeatedMember (n, FStar_Pervasives_Native.None)) repeated))
     end)))

type ccase<'num, 'flt, 't> = {ctag : Prims.string; write_case : 't  ->  FStar_Pervasives_Native.option<result<Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)>, fault>>; read_case : Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)>  ->  result<'t, Prims.list<derr>>; case_names : Prims.list<Prims.string>}


let __proj__Mkccase__item__ctag = (fun ( projectee  :  ccase<'num, 'flt, 't> ) -> (match (projectee) with
| {ctag = ctag; write_case = write_case; read_case = read_case; case_names = case_names} -> begin
     ctag
     end))


let __proj__Mkccase__item__write_case = (fun ( projectee  :  ccase<'num, 'flt, 't> ) -> (match (projectee) with
| {ctag = ctag; write_case = write_case; read_case = read_case; case_names = case_names} -> begin
     write_case
     end))


let __proj__Mkccase__item__read_case = (fun ( projectee  :  ccase<'num, 'flt, 't> ) -> (match (projectee) with
| {ctag = ctag; write_case = write_case; read_case = read_case; case_names = case_names} -> begin
     read_case
     end))


let __proj__Mkccase__item__case_names = (fun ( projectee  :  ccase<'num, 'flt, 't> ) -> (match (projectee) with
| {ctag = ctag; write_case = write_case; read_case = read_case; case_names = case_names} -> begin
     case_names
     end))


let c_case = (fun ( tag  :  Prims.string ) ( project  :  't  ->  FStar_Pervasives_Native.option<'p> ) ( inject  :  'p  ->  't ) ( fs  :  fields<'num, 'flt, 'p, 'p> ) -> {ctag = tag; write_case = (fun ( v  :  't ) -> (match ((project v)) with
| FStar_Pervasives_Native.Some (x) -> begin
     FStar_Pervasives_Native.Some ((fs.write_fields x))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)); read_case = (fun ( ms  :  Prims.list<(Prims.string * WireDecode.jval<'num, 'flt>)> ) -> (match ((fs.read_fields ms)) with
| Ok (x) -> begin
     Ok ((inject x))
     end
| Error (e) -> begin
     Error (e)
     end)); case_names = fs.names})


let rec tags = (fun ( cases  :  Prims.list<ccase<'num, 'flt, 't>> ) -> (match (cases) with
| [] -> begin
     []
     end
| (c)::rest -> begin
     (c.ctag)::(tags rest)
     end))


let union_faults = (fun ( key  :  Prims.string ) ( cases  :  Prims.list<ccase<'num, 'flt, 't>> ) -> (app (concat_map (fun ( c  :  ccase<'num, 'flt, 't> ) -> (map_l (fun ( n  :  Prims.string ) -> RepeatedMember (n, FStar_Pervasives_Native.Some (c.ctag))) (repeats c.case_names))) cases) (app (map_l (fun ( g  :  Prims.string ) -> RepeatedTag (g)) (repeats (tags cases))) (concat_map (fun ( c  :  ccase<'num, 'flt, 't> ) ->  
if (mem key c.case_names) then begin
     (DiscriminatorAsMember (c.ctag, key))::[]
     end else begin
     []
     end) cases))))


let rec case_table = (fun ( cases  :  Prims.list<ccase<'num, 'flt, 't>> ) -> (match (cases) with
| [] -> begin
     []
     end
| (c)::rest -> begin
     (((c.ctag), ((succeed c))))::(case_table rest)
     end))


let rec try_write_cases = (fun ( key  :  Prims.string ) ( cases  :  Prims.list<ccase<'num, 'flt, 't>> ) ( v  :  't ) -> (match (cases) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::rest -> begin
     (match ((c.write_case v)) with
| FStar_Pervasives_Native.Some (r) -> begin
     FStar_Pervasives_Native.Some ((match (r) with
| Ok (fs) -> begin
     Ok (WireDecode.JObj ((((key), (WireDecode.JStr (c.ctag))))::fs))
     end
| Error (f) -> begin
     Error (f)
     end))
     end
| FStar_Pervasives_Native.None -> begin
     (try_write_cases key rest v)
     end)
     end))


let union_read = (fun ( key  :  Prims.string ) ( cases  :  Prims.list<ccase<'num, 'flt, 't>> ) ( j  :  WireDecode.jval<'num, 'flt> ) -> (match ((tag_dispatch key (case_table cases) j)) with
| Error (e) -> begin
     Error ((e)::[])
     end
| Ok (c) -> begin
     (strict_object ((key)::c.case_names) j c.read_case)
     end))


let c_union = (fun ( key  :  Prims.string ) ( cases  :  Prims.list<ccase<'num, 'flt, 't>> ) -> (match ((union_faults key cases)) with
| [] -> begin
     Ok ({write = (fun ( v  :  't ) -> (match ((try_write_cases key cases v)) with
| FStar_Pervasives_Native.Some (r) -> begin
     r
     end
| FStar_Pervasives_Native.None -> begin
     Error (Unrecognised ([]))
     end)); read_all = (union_read key cases)})
     end
| faults -> begin
     Error (faults)
     end))

type twin_pt = {px : Prims.string; py : FStar_Pervasives_Native.option<Prims.int>}


let __proj__Mktwin_pt__item__px : twin_pt  ->  Prims.string = (fun ( projectee  :  twin_pt ) -> (match (projectee) with
| {px = px; py = py} -> begin
     px
     end))


let __proj__Mktwin_pt__item__py : twin_pt  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  twin_pt ) -> (match (projectee) with
| {px = px; py = py} -> begin
     py
     end))


let twin_pt_fields : fields<Prims.int, Prims.bool, twin_pt, twin_pt> = (c_opt_field "y" (fun ( p  :  twin_pt ) -> p.py) (c_int ()) (c_field "x" (fun ( p  :  twin_pt ) -> p.px) (c_string ()) (c_record (fun ( x  :  Prims.string ) ( y  :  FStar_Pervasives_Native.option<Prims.int> ) -> {px = x; py = y}))))

type twin_shape =
| Circle of Prims.int
| Square of Prims.int


let uu___is_Circle : twin_shape  ->  Prims.bool = (fun ( projectee  :  twin_shape ) -> (match (projectee) with
| Circle (r) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Circle__item__r : twin_shape  ->  Prims.int = (fun ( projectee  :  twin_shape ) -> (match (projectee) with
| Circle (r) -> begin
     r
     end))


let uu___is_Square : twin_shape  ->  Prims.bool = (fun ( projectee  :  twin_shape ) -> (match (projectee) with
| Square (s) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Square__item__s : twin_shape  ->  Prims.int = (fun ( projectee  :  twin_shape ) -> (match (projectee) with
| Square (s) -> begin
     s
     end))


let twin_payload : fields<Prims.int, Prims.bool, Prims.int, Prims.int> = (c_field "size" (fun ( n  :  Prims.int ) -> n) (c_int ()) (c_record (fun ( n  :  Prims.int ) -> n)))


let twin_project_circle : twin_shape  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( v  :  twin_shape ) -> (match (v) with
| Circle (r) -> begin
     FStar_Pervasives_Native.Some (r)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let twin_project_square : twin_shape  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( v  :  twin_shape ) -> (match (v) with
| Square (s) -> begin
     FStar_Pervasives_Native.Some (s)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let twin_shapes : Prims.list<ccase<Prims.int, Prims.bool, twin_shape>> = ((c_case "circle" twin_project_circle (fun ( r  :  Prims.int ) -> Circle (r)) twin_payload))::((c_case "square" twin_project_square (fun ( s  :  Prims.int ) -> Square (s)) twin_payload))::[]


let twin_doc : WireDecode.jval<Prims.int, Prims.bool> = WireDecode.JObj (((("a"), (WireDecode.JStr ("x"))))::((("b"), (WireDecode.JArr ((WireDecode.JInt ((Prims.parse_int "1")))::(WireDecode.JStr ("y"))::[]))))::[])

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


let twin_record_round_trips : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((c_build twin_pt_fields)) with
| Ok (c) -> begin
     (match ((c.write {px = "p"; py = FStar_Pervasives_Native.Some ((Prims.parse_int "3"))})) with
| Ok (j) -> begin
     (Prims.op_Equals (c.read_all j) (Ok ({px = "p"; py = FStar_Pervasives_Native.Some ((Prims.parse_int "3"))})))
     end
| Error (uu___1) -> begin
     false
     end)
     end
| Error (uu___1) -> begin
     false
     end))


let twin_record_collects : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((c_build twin_pt_fields)) with
| Ok (c) -> begin
     (Prims.op_Equals (c.read_all (WireDecode.JObj (((("x"), (WireDecode.JInt ((Prims.parse_int "1")))))::((("y"), (WireDecode.JStr ("s"))))::((("z"), (WireDecode.JBool (true))))::[]))) (Error (({code = UndeclaredMember; path = (Key ("z"))::[]})::({code = WrongKind; path = (Key ("x"))::[]})::({code = WrongKind; path = (Key ("y"))::[]})::[])))
     end
| Error (uu___1) -> begin
     false
     end))


let twin_union_round_trips : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((c_union "kind" twin_shapes)) with
| Ok (c) -> begin
     ((Prims.op_Equals (c.write (Square ((Prims.parse_int "2")))) (Ok (WireDecode.JObj (((("kind"), (WireDecode.JStr ("square"))))::((("size"), (WireDecode.JInt ((Prims.parse_int "2")))))::[])))) && (Prims.op_Equals (c.read_all (WireDecode.JObj (((("kind"), (WireDecode.JStr ("square"))))::((("size"), (WireDecode.JInt ((Prims.parse_int "2")))))::[]))) (Ok (Square ((Prims.parse_int "2"))))))
     end
| Error (uu___1) -> begin
     false
     end))


let twin_union_unknown_tag : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((c_union "kind" twin_shapes)) with
| Ok (c) -> begin
     (Prims.op_Equals (c.read_all (WireDecode.JObj (((("kind"), (WireDecode.JStr ("tri"))))::[]))) (Error (({code = UnknownTag; path = (Key ("kind"))::[]})::[])))
     end
| Error (uu___1) -> begin
     false
     end))


let twin_enum_repeated : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((c_enum (((("a"), ((Prims.parse_int "1"))))::((("a"), ((Prims.parse_int "2"))))::((("b"), ((Prims.parse_int "3"))))::[]))) with
| Ok (uu___1) -> begin
     false
     end
| Error (fs) -> begin
     (Prims.op_Equals fs ((RepeatedSpelling ("a"))::[]))
     end))


let twin_union_discriminator : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((c_union "size" twin_shapes)) with
| Ok (uu___1) -> begin
     false
     end
| Error (fs) -> begin
     (Prims.op_Equals fs ((DiscriminatorAsMember ("circle", "size"))::(DiscriminatorAsMember ("square", "size"))::[]))
     end))


let twins : Prims.list<twin> = ({tname = "field-refuses-an-item-under-its-member-and-index"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (field "b" (list_d int_d) twin_doc) (Error ({code = WrongKind; path = (Key ("b"))::(Index ((Prims.parse_int "1")))::[]}))))})::({tname = "the-item-refusal-resolves"; tholds = (fun ( uu___  :  unit ) -> (resolves_in twin_doc {code = WrongKind; path = (Key ("b"))::(Index ((Prims.parse_int "1")))::[]}))})::({tname = "missing-names-the-absent-member-and-resolves"; tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (field "c" str twin_doc) (Error ((missing "c")))) && (resolves_in twin_doc (missing "c"))))})::({tname = "missing-of-a-present-member-does-not-resolve"; tholds = (fun ( uu___  :  unit ) -> (not ((resolves_in twin_doc (missing "a")))))})::({tname = "at-walks-into-an-item"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (at ((Key ("b"))::(Index ((Prims.parse_int "0")))::[]) int_d twin_doc) (Ok ((Prims.parse_int "1")))))})::({tname = "record-round-trips"; tholds = twin_record_round_trips})::({tname = "record-collects-every-defect-undeclared-first"; tholds = twin_record_collects})::({tname = "union-writes-its-tag-first-and-reads-back"; tholds = twin_union_round_trips})::({tname = "union-refuses-an-unknown-tag-at-the-discriminator"; tholds = twin_union_unknown_tag})::({tname = "enum-refuses-a-repeated-spelling"; tholds = twin_enum_repeated})::({tname = "union-refuses-the-discriminator-as-a-member"; tholds = twin_union_discriminator})::[]




