module WireDecode
type outcome<'a> =
| Ok of 'a
| Error of Prims.string


let uu___is_Ok = (fun ( projectee  :  outcome<'a> ) -> (match (projectee) with
| Ok (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ok__item__v = (fun ( projectee  :  outcome<'a> ) -> (match (projectee) with
| Ok (v) -> begin
     v
     end))


let uu___is_Error = (fun ( projectee  :  outcome<'a> ) -> (match (projectee) with
| Error (msg) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Error__item__msg = (fun ( projectee  :  outcome<'a> ) -> (match (projectee) with
| Error (msg) -> begin
     msg
     end))


let bind = (fun ( r  :  outcome<'a> ) ( f  :  'a  ->  outcome<'b> ) -> (match (r) with
| Ok (v) -> begin
     (f v)
     end
| Error (m) -> begin
     Error (m)
     end))


let rec rev_app = (fun ( l  :  Prims.list<'a> ) ( acc  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     acc
     end
| (x)::t -> begin
     (rev_app t ((x)::acc))
     end))


let rev = (fun ( l  :  Prims.list<'a> ) -> (rev_app l []))

type jval<'num, 'flt> =
| JStr of Prims.string
| JInt of 'num
| JBool of Prims.bool
| JFloat of 'flt
| JArr of Prims.list<jval<'num, 'flt>>
| JObj of Prims.list<(Prims.string * jval<'num, 'flt>)>


let uu___is_JStr = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JStr (s) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JStr__item__s = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JStr (s) -> begin
     s
     end))


let uu___is_JInt = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JInt (i) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JInt__item__i = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JInt (i) -> begin
     i
     end))


let uu___is_JBool = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JBool (b) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JBool__item__b = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JBool (b) -> begin
     b
     end))


let uu___is_JFloat = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JFloat (f) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JFloat__item__f = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JFloat (f) -> begin
     f
     end))


let uu___is_JArr = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JArr (items) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JArr__item__items = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JArr (items) -> begin
     items
     end))


let uu___is_JObj = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JObj (fields) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JObj__item__fields = (fun ( projectee  :  jval<'num, 'flt> ) -> (match (projectee) with
| JObj (fields) -> begin
     fields
     end))


let kind_name = (fun ( v  :  jval<'num, 'flt> ) -> (match (v) with
| JStr (uu___) -> begin
     "string"
     end
| JInt (uu___) -> begin
     "int"
     end
| JBool (uu___) -> begin
     "bool"
     end
| JFloat (uu___) -> begin
     "float"
     end
| JArr (uu___) -> begin
     "array"
     end
| JObj (uu___) -> begin
     "object"
     end))


let rec find_field = (fun ( name  :  Prims.string ) ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     Error ((Prims.strcat "missing property: " name))
     end
| ((k, v))::t -> begin
      
if (Prims.op_Equals k name) then begin
     Ok (v)
     end else begin
     (find_field name t)
     end
     end))


let get_prop = (fun ( name  :  Prims.string ) ( el  :  jval<'num, 'flt> ) -> (match (el) with
| JObj (fields) -> begin
     (find_field name fields)
     end
| other -> begin
     Error ((Prims.strcat "expected object, got " (kind_name other)))
     end))


let as_string = (fun ( el  :  jval<'num, 'flt> ) -> (match (el) with
| JStr (s) -> begin
     Ok (s)
     end
| other -> begin
     Error ((Prims.strcat "expected string, got " (kind_name other)))
     end))


let as_int = (fun ( el  :  jval<'num, 'flt> ) -> (match (el) with
| JInt (i) -> begin
     Ok (i)
     end
| other -> begin
     Error ((Prims.strcat "expected int, got " (kind_name other)))
     end))


let as_bool = (fun ( el  :  jval<'num, 'flt> ) -> (match (el) with
| JBool (b) -> begin
     Ok (b)
     end
| other -> begin
     Error ((Prims.strcat "expected bool, got " (kind_name other)))
     end))


let as_float = (fun ( to_flt  :  'num  ->  'flt ) ( el  :  jval<'num, 'flt> ) -> (match (el) with
| JFloat (f) -> begin
     Ok (f)
     end
| JInt (i) -> begin
     Ok ((to_flt i))
     end
| other -> begin
     Error ((Prims.strcat "expected number, got " (kind_name other)))
     end))


let kind_of = (fun ( el  :  jval<'num, 'flt> ) -> (bind (get_prop "kind" el) as_string))


let str_field = (fun ( name  :  Prims.string ) ( el  :  jval<'num, 'flt> ) -> (bind (get_prop name el) as_string))


let int_field = (fun ( name  :  Prims.string ) ( el  :  jval<'num, 'flt> ) -> (bind (get_prop name el) as_int))


let rec map_list_go = (fun ( d  :  jval<'num, 'flt>  ->  outcome<'t> ) ( acc  :  Prims.list<'t> ) ( xs  :  Prims.list<jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     Ok ((rev acc))
     end
| (x)::rest -> begin
     (match ((d x)) with
| Ok (v) -> begin
     (map_list_go d ((v)::acc) rest)
     end
| Error (m) -> begin
     Error (m)
     end)
     end))


let map_list = (fun ( d  :  jval<'num, 'flt>  ->  outcome<'t> ) ( el  :  jval<'num, 'flt> ) -> (match (el) with
| JArr (items) -> begin
     (map_list_go d [] items)
     end
| other -> begin
     Error ((Prims.strcat "expected array, got " (kind_name other)))
     end))

type rnode =
| RText of Prims.string
| RFlag of Prims.bool
| RTags of Prims.list<Prims.string>
| RGroup of Prims.string * Prims.list<rnode>


let uu___is_RText : rnode  ->  Prims.bool = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RText (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RText__item__value : rnode  ->  Prims.string = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RText (value) -> begin
     value
     end))


let uu___is_RFlag : rnode  ->  Prims.bool = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RFlag (on) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RFlag__item__on : rnode  ->  Prims.bool = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RFlag (on) -> begin
     on
     end))


let uu___is_RTags : rnode  ->  Prims.bool = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RTags (tags) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RTags__item__tags : rnode  ->  Prims.list<Prims.string> = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RTags (tags) -> begin
     tags
     end))


let uu___is_RGroup : rnode  ->  Prims.bool = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RGroup (id, items) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RGroup__item__id : rnode  ->  Prims.string = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RGroup (id, items) -> begin
     id
     end))


let __proj__RGroup__item__items : rnode  ->  Prims.list<rnode> = (fun ( projectee  :  rnode ) -> (match (projectee) with
| RGroup (id, items) -> begin
     items
     end))


let rec encode = (fun ( n  :  rnode ) -> (match (n) with
| RText (s) -> begin
     JObj (((("kind"), (JStr ("text"))))::((("value"), (JStr (s))))::[])
     end
| RFlag (b) -> begin
     JObj (((("kind"), (JStr ("flag"))))::((("on"), (JBool (b))))::[])
     end
| RTags (ts) -> begin
     JObj (((("kind"), (JStr ("tags"))))::((("tags"), (JArr ((encode_tags ts)))))::[])
     end
| RGroup (id, items) -> begin
     JObj (((("kind"), (JStr ("group"))))::((("id"), (JStr (id))))::((("items"), (JArr ((encode_items items)))))::[])
     end))
and encode_tags = (fun ( ts  :  Prims.list<Prims.string> ) -> (match (ts) with
| [] -> begin
     []
     end
| (s)::t -> begin
     (JStr (s))::(encode_tags t)
     end))
and encode_items = (fun ( ns  :  Prims.list<rnode> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     ((encode n))::(encode_items t)
     end))


let rec decode_node = (fun ( el  :  jval<'num, 'flt> ) -> (match ((kind_of el)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (tag) -> begin
      
if (Prims.op_Equals tag "text") then begin
     (match ((str_field "value" el)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (s) -> begin
     Ok (RText (s))
     end)
     end else begin
      
if (Prims.op_Equals tag "flag") then begin
     (match ((get_prop "on" el)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (v) -> begin
     (match ((as_bool v)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (b) -> begin
     Ok (RFlag (b))
     end)
     end)
     end else begin
      
if (Prims.op_Equals tag "tags") then begin
     (match ((get_prop "tags" el)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (v) -> begin
     (match ((map_list as_string v)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (ts) -> begin
     Ok (RTags (ts))
     end)
     end)
     end else begin
      
if (Prims.op_Equals tag "group") then begin
     (match ((str_field "id" el)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (id) -> begin
     (

let r = (get_prop "items" el)
in (match (r) with
| Error (m) -> begin
     Error (m)
     end
| Ok (v) -> begin
     (match (v) with
| JArr (ys) -> begin
     (match ((decode_items [] ys)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (ns) -> begin
     Ok (RGroup (id, ns))
     end)
     end
| other -> begin
     Error ((Prims.strcat "expected array, got " (kind_name other)))
     end)
     end))
     end)
     end else begin
     Error ((Prims.strcat "unknown kind: " tag))
     end
     end
     end
     end
     end))
and decode_items = (fun ( acc  :  Prims.list<rnode> ) ( ys  :  Prims.list<jval<'num, 'flt>> ) -> (match (ys) with
| [] -> begin
     Ok ((rev acc))
     end
| (y)::rest -> begin
     (match ((decode_node y)) with
| Ok (n) -> begin
     (decode_items ((n)::acc) rest)
     end
| Error (m) -> begin
     Error (m)
     end)
     end))


let rec all_ok = (fun ( d  :  jval<'num, 'flt>  ->  outcome<'t> ) ( xs  :  Prims.list<jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::rest -> begin
     ((match ((d x)) with
| Ok (v) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_ok d rest))
     end))

type null_policy =
| RejectNull
| EraseMemberNull


let uu___is_RejectNull : null_policy  ->  Prims.bool = (fun ( projectee  :  null_policy ) -> (match (projectee) with
| RejectNull -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_EraseMemberNull : null_policy  ->  Prims.bool = (fun ( projectee  :  null_policy ) -> (match (projectee) with
| EraseMemberNull -> begin
     true
     end
| uu___ -> begin
     false
     end))

type jvaln<'num, 'flt> =
| NStr of Prims.string
| NInt of 'num
| NBool of Prims.bool
| NFloat of 'flt
| NArr of Prims.list<jvaln<'num, 'flt>>
| NObj of Prims.list<(Prims.string * jvaln<'num, 'flt>)>
| NNull


let uu___is_NStr = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NStr (s) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NStr__item__s = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NStr (s) -> begin
     s
     end))


let uu___is_NInt = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NInt (i) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NInt__item__i = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NInt (i) -> begin
     i
     end))


let uu___is_NBool = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NBool (b) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NBool__item__b = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NBool (b) -> begin
     b
     end))


let uu___is_NFloat = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NFloat (f) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NFloat__item__f = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NFloat (f) -> begin
     f
     end))


let uu___is_NArr = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NArr (items) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NArr__item__items = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NArr (items) -> begin
     items
     end))


let uu___is_NObj = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NObj (fields) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NObj__item__fields = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NObj (fields) -> begin
     fields
     end))


let uu___is_NNull = (fun ( projectee  :  jvaln<'num, 'flt> ) -> (match (projectee) with
| NNull -> begin
     true
     end
| uu___ -> begin
     false
     end))


let reject_msg : Prims.string = "null is not representable in the Fuaran wire JVal model"


let no_absence_msg : Prims.string = "null is not representable in the Fuaran wire JVal model, and this position has no absence to erase it to (only an object-member null is erased)"


let rec read = (fun ( p  :  null_policy ) ( d  :  jvaln<'num, 'flt> ) -> (match (d) with
| NNull -> begin
     Error ( 
if (match (p) with
| EraseMemberNull -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     no_absence_msg
     end else begin
     reject_msg
     end)
     end
| NStr (s) -> begin
     Ok (JStr (s))
     end
| NInt (i) -> begin
     Ok (JInt (i))
     end
| NBool (b) -> begin
     Ok (JBool (b))
     end
| NFloat (f) -> begin
     Ok (JFloat (f))
     end
| NArr (xs) -> begin
     (match ((read_items p xs)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (vs) -> begin
     Ok (JArr (vs))
     end)
     end
| NObj (fs) -> begin
     (match ((read_fields p fs)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (kvs) -> begin
     Ok (JObj (kvs))
     end)
     end))
and read_items = (fun ( p  :  null_policy ) ( xs  :  Prims.list<jvaln<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     Ok ([])
     end
| (x)::t -> begin
     (match ((read p x)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (v) -> begin
     (match ((read_items p t)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (vs) -> begin
     Ok ((v)::vs)
     end)
     end)
     end))
and read_fields = (fun ( p  :  null_policy ) ( fs  :  Prims.list<(Prims.string * jvaln<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     Ok ([])
     end
| ((k, v))::t -> begin
      
if ((match (p) with
| EraseMemberNull -> begin
     true
     end
| uu___ -> begin
     false
     end) && (match (v) with
| NNull -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     (read_fields p t)
     end else begin
     (match ((read p v)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (jv) -> begin
     (match ((read_fields p t)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (r) -> begin
     Ok ((((k), (jv)))::r)
     end)
     end)
     end
     end))


let rec extract_field = (fun ( name  :  Prims.string ) ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((k, v))::t -> begin
      
if (Prims.op_Equals k name) then begin
     FStar_Pervasives_Native.Some (((v), (t)))
     end else begin
     (match ((extract_field name t)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (w, rest) -> begin
     FStar_Pervasives_Native.Some (((w), ((((k), (v)))::rest)))
     end)
     end
     end))


let rec member_perm = (fun ( a  :  jval<'num, 'flt> ) ( b  :  jval<'num, 'flt> ) -> (match (((a), (b))) with
| (JStr (x), JStr (y)) -> begin
     (Prims.op_Equals x y)
     end
| (JInt (x), JInt (y)) -> begin
     (Prims.op_Equals x y)
     end
| (JBool (x), JBool (y)) -> begin
     (Prims.op_Equals x y)
     end
| (JFloat (x), JFloat (y)) -> begin
     (Prims.op_Equals x y)
     end
| (JArr (xs), JArr (ys)) -> begin
     (items_perm xs ys)
     end
| (JObj (fs), JObj (gs)) -> begin
     (fields_perm fs gs)
     end
| (uu___, uu___1) -> begin
     false
     end))
and items_perm = (fun ( xs  :  Prims.list<jval<'num, 'flt>> ) ( ys  :  Prims.list<jval<'num, 'flt>> ) -> (match (((xs), (ys))) with
| ([], []) -> begin
     true
     end
| ((x)::xt, (y)::yt) -> begin
     ((member_perm x y) && (items_perm xt yt))
     end
| (uu___, uu___1) -> begin
     false
     end))
and fields_perm = (fun ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) ( gs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     (match (gs) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| ((k, v))::t -> begin
     (match ((extract_field k gs)) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (w, rest) -> begin
     ((member_perm v w) && (fields_perm t rest))
     end)
     end))


let outcome_perm = (fun ( r1  :  outcome<jval<'num, 'flt>> ) ( r2  :  outcome<jval<'num, 'flt>> ) -> (match (((r1), (r2))) with
| (Ok (v1), Ok (v2)) -> begin
     (member_perm v1 v2)
     end
| (Error (m1), Error (m2)) -> begin
     (Prims.op_Equals m1 m2)
     end
| (uu___, uu___1) -> begin
     false
     end))


let rec has_key = (fun ( name  :  Prims.string ) ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     false
     end
| ((k, uu___))::t -> begin
     ((Prims.op_Equals k name) || (has_key name t))
     end))


let rec keys_unique = (fun ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     true
     end
| ((k, uu___))::t -> begin
     ((not ((has_key k t))) && (keys_unique t))
     end))


let rec keys_unique_deep = (fun ( a  :  jval<'num, 'flt> ) -> (match (a) with
| JArr (xs) -> begin
     (keys_unique_deep_items xs)
     end
| JObj (fs) -> begin
     ((keys_unique fs) && (keys_unique_deep_fields fs))
     end
| uu___ -> begin
     true
     end))
and keys_unique_deep_items = (fun ( xs  :  Prims.list<jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((keys_unique_deep x) && (keys_unique_deep_items t))
     end))
and keys_unique_deep_fields = (fun ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     true
     end
| ((uu___, v))::t -> begin
     ((keys_unique_deep v) && (keys_unique_deep_fields t))
     end))


let rec remove_member = (fun ( m  :  (Prims.string * jval<'num, 'flt>) ) ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::t -> begin
      
if (Prims.op_Equals x m) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     (match ((remove_member m t)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (r) -> begin
     FStar_Pervasives_Native.Some ((x)::r)
     end)
     end
     end))


let rec list_perm = (fun ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) ( gs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     (match (gs) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| (x)::t -> begin
     (match ((remove_member x gs)) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (r) -> begin
     (list_perm t r)
     end)
     end))


let rec key_count = (fun ( name  :  Prims.string ) ( fs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| ((k, uu___))::t -> begin
     (( 
if (Prims.op_Equals k name) then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "0")
     end) + (key_count name t))
     end))


let rec fields_pointwise = (fun ( hs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) ( gs  :  Prims.list<(Prims.string * jval<'num, 'flt>)> ) -> (match (((hs), (gs))) with
| ([], []) -> begin
     true
     end
| (((k1, v1))::t1, ((k2, v2))::t2) -> begin
     (((Prims.op_Equals k1 k2) && (member_perm v1 v2)) && (fields_pointwise t1 t2))
     end
| (uu___, uu___1) -> begin
     false
     end))




