module WireCanon
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
     ((Prims.op_Equals x y) || (mem x t))
     end))

type hexd =
| HD0
| HD1
| HD2
| HD3
| HD4
| HD5
| HD6
| HD7
| HD8
| HD9
| HDa
| HDb
| HDc
| HDd
| HDe
| HDf


let uu___is_HD0 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD0 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD1 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD1 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD2 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD2 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD3 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD3 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD4 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD4 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD5 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD5 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD6 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD6 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD7 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD7 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD8 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD8 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HD9 : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HD9 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HDa : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HDa -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HDb : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HDb -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HDc : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HDc -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HDd : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HDd -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HDe : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HDe -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_HDf : hexd  ->  Prims.bool = (fun ( projectee  :  hexd ) -> (match (projectee) with
| HDf -> begin
     true
     end
| uu___ -> begin
     false
     end))


let hexd_str : hexd  ->  Prims.string = (fun ( d  :  hexd ) -> (match (d) with
| HD0 -> begin
     "0"
     end
| HD1 -> begin
     "1"
     end
| HD2 -> begin
     "2"
     end
| HD3 -> begin
     "3"
     end
| HD4 -> begin
     "4"
     end
| HD5 -> begin
     "5"
     end
| HD6 -> begin
     "6"
     end
| HD7 -> begin
     "7"
     end
| HD8 -> begin
     "8"
     end
| HD9 -> begin
     "9"
     end
| HDa -> begin
     "a"
     end
| HDb -> begin
     "b"
     end
| HDc -> begin
     "c"
     end
| HDd -> begin
     "d"
     end
| HDe -> begin
     "e"
     end
| HDf -> begin
     "f"
     end))


let is_dec : hexd  ->  Prims.bool = (fun ( d  :  hexd ) -> (match (d) with
| HDa -> begin
     false
     end
| HDb -> begin
     false
     end
| HDc -> begin
     false
     end
| HDd -> begin
     false
     end
| HDe -> begin
     false
     end
| HDf -> begin
     false
     end
| uu___ -> begin
     true
     end))

type ch =
| CQuote
| CBackslash
| CLBrace
| CRBrace
| CLBrack
| CRBrack
| CColon
| CComma
| CMinus
| CPlus
| CDot
| CUpE
| CHexCh of hexd
| CLu
| CCtrl of Prims.bool * hexd
| CPlain of Prims.string


let uu___is_CQuote : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CQuote -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CBackslash : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CBackslash -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLBrace : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLBrace -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CRBrace : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CRBrace -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLBrack : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLBrack -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CRBrack : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CRBrack -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CColon : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CColon -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CComma : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CComma -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CMinus : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CMinus -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CPlus : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CPlus -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CDot : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CDot -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CUpE : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CUpE -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CHexCh : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CHexCh (d) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CHexCh__item__d : ch  ->  hexd = (fun ( projectee  :  ch ) -> (match (projectee) with
| CHexCh (d) -> begin
     d
     end))


let uu___is_CLu : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLu -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CCtrl : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CCtrl (hi, lo) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CCtrl__item__hi : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CCtrl (hi, lo) -> begin
     hi
     end))


let __proj__CCtrl__item__lo : ch  ->  hexd = (fun ( projectee  :  ch ) -> (match (projectee) with
| CCtrl (hi, lo) -> begin
     lo
     end))


let uu___is_CPlain : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CPlain (c) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CPlain__item__c : ch  ->  Prims.string = (fun ( projectee  :  ch ) -> (match (projectee) with
| CPlain (c) -> begin
     c
     end))


let denot : ch  ->  Prims.string = (fun ( c  :  ch ) -> (match (c) with
| CQuote -> begin
     "\""
     end
| CBackslash -> begin
     "\\"
     end
| CLBrace -> begin
     "{"
     end
| CRBrace -> begin
     "}"
     end
| CLBrack -> begin
     "["
     end
| CRBrack -> begin
     "]"
     end
| CColon -> begin
     ":"
     end
| CComma -> begin
     ","
     end
| CMinus -> begin
     "-"
     end
| CPlus -> begin
     "+"
     end
| CDot -> begin
     "."
     end
| CUpE -> begin
     "E"
     end
| CHexCh (d) -> begin
     (hexd_str d)
     end
| CLu -> begin
     "u"
     end
| CCtrl (hi, lo) -> begin
     (Prims.strcat ( 
if hi then begin
     "\\u0001"
     end else begin
     "\\u0000"
     end) (hexd_str lo))
     end
| CPlain (s) -> begin
     s
     end))


let reserved_spellings : Prims.list<Prims.string> = ("\"")::("\\")::("{")::("}")::("[")::("]")::(":")::(",")::("-")::("+")::(".")::("E")::("u")::("0")::("1")::("2")::("3")::("4")::("5")::("6")::("7")::("8")::("9")::("a")::("b")::("c")::("d")::("e")::("f")::[]


let bridged : ch  ->  Prims.bool = (fun ( c  :  ch ) -> (match (c) with
| CPlain (s) -> begin
     (not ((mem s reserved_spellings)))
     end
| uu___ -> begin
     true
     end))


let rec bridged_all : Prims.list<ch>  ->  Prims.bool = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((bridged c) && (bridged_all t))
     end))

type jval<'num, 'flt> =
| JStr of Prims.list<ch>
| JInt of 'num
| JBool of Prims.bool
| JFloat of 'flt
| JArr of Prims.list<jval<'num, 'flt>>
| JObj of Prims.list<(Prims.list<ch> * jval<'num, 'flt>)>


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

type fcls =
| FNaN
| FPosInf
| FNegInf
| FFinite


let uu___is_FNaN : fcls  ->  Prims.bool = (fun ( projectee  :  fcls ) -> (match (projectee) with
| FNaN -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FPosInf : fcls  ->  Prims.bool = (fun ( projectee  :  fcls ) -> (match (projectee) with
| FPosInf -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FNegInf : fcls  ->  Prims.bool = (fun ( projectee  :  fcls ) -> (match (projectee) with
| FNegInf -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FFinite : fcls  ->  Prims.bool = (fun ( projectee  :  fcls ) -> (match (projectee) with
| FFinite -> begin
     true
     end
| uu___ -> begin
     false
     end))

type wire<'num, 'flt> = {int_str : 'num  ->  Prims.list<ch>; float_str : 'flt  ->  Prims.list<ch>; fclass : 'flt  ->  fcls; is_zero : 'flt  ->  Prims.bool; pos_zero : 'flt; key_le : Prims.list<ch>  ->  Prims.list<ch>  ->  Prims.bool; tok_read : Prims.list<ch>  ->  outcome<jval<'num, 'flt>>}


let __proj__Mkwire__item__int_str = (fun ( projectee  :  wire<'num, 'flt> ) -> (match (projectee) with
| {int_str = int_str; float_str = float_str; fclass = fclass; is_zero = is_zero; pos_zero = pos_zero; key_le = key_le; tok_read = tok_read} -> begin
     int_str
     end))


let __proj__Mkwire__item__float_str = (fun ( projectee  :  wire<'num, 'flt> ) -> (match (projectee) with
| {int_str = int_str; float_str = float_str; fclass = fclass; is_zero = is_zero; pos_zero = pos_zero; key_le = key_le; tok_read = tok_read} -> begin
     float_str
     end))


let __proj__Mkwire__item__fclass = (fun ( projectee  :  wire<'num, 'flt> ) -> (match (projectee) with
| {int_str = int_str; float_str = float_str; fclass = fclass; is_zero = is_zero; pos_zero = pos_zero; key_le = key_le; tok_read = tok_read} -> begin
     fclass
     end))


let __proj__Mkwire__item__is_zero = (fun ( projectee  :  wire<'num, 'flt> ) -> (match (projectee) with
| {int_str = int_str; float_str = float_str; fclass = fclass; is_zero = is_zero; pos_zero = pos_zero; key_le = key_le; tok_read = tok_read} -> begin
     is_zero
     end))


let __proj__Mkwire__item__pos_zero = (fun ( projectee  :  wire<'num, 'flt> ) -> (match (projectee) with
| {int_str = int_str; float_str = float_str; fclass = fclass; is_zero = is_zero; pos_zero = pos_zero; key_le = key_le; tok_read = tok_read} -> begin
     pos_zero
     end))


let __proj__Mkwire__item__key_le = (fun ( projectee  :  wire<'num, 'flt> ) -> (match (projectee) with
| {int_str = int_str; float_str = float_str; fclass = fclass; is_zero = is_zero; pos_zero = pos_zero; key_le = key_le; tok_read = tok_read} -> begin
     key_le
     end))


let __proj__Mkwire__item__tok_read = (fun ( projectee  :  wire<'num, 'flt> ) -> (match (projectee) with
| {int_str = int_str; float_str = float_str; fclass = fclass; is_zero = is_zero; pos_zero = pos_zero; key_le = key_le; tok_read = tok_read} -> begin
     tok_read
     end))


let key_lt = (fun ( w  :  wire<'num, 'flt> ) ( a  :  Prims.list<ch> ) ( b  :  Prims.list<ch> ) -> ((w.key_le a b) && (not ((w.key_le b a)))))


let esc_ch : ch  ->  Prims.list<ch> = (fun ( c  :  ch ) -> (match (c) with
| CQuote -> begin
     (CBackslash)::(CQuote)::[]
     end
| CBackslash -> begin
     (CBackslash)::(CBackslash)::[]
     end
| CCtrl (hi, lo) -> begin
     (CBackslash)::(CLu)::(CHexCh (HD0))::(CHexCh (HD0))::(CHexCh ( 
if hi then begin
     HD1
     end else begin
     HD0
     end))::(CHexCh (lo))::[]
     end
| other -> begin
     (other)::[]
     end))


let rec escape : Prims.list<ch>  ->  Prims.list<ch> = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (app (esc_ch c) (escape t))
     end))


let quoted : Prims.list<ch>  ->  Prims.list<ch> = (fun ( s  :  Prims.list<ch> ) -> (CQuote)::(app (escape s) ((CQuote)::[])))


let nan_chars : Prims.list<ch> = (CPlain ("N"))::(CHexCh (HDa))::(CPlain ("N"))::[]


let inf_chars : Prims.list<ch> = (CPlain ("I"))::(CPlain ("n"))::(CHexCh (HDf))::(CPlain ("i"))::(CPlain ("n"))::(CPlain ("i"))::(CPlain ("t"))::(CPlain ("y"))::[]


let neg_inf_chars : Prims.list<ch> = (CMinus)::inf_chars


let true_chars : Prims.list<ch> = (CPlain ("t"))::(CPlain ("r"))::(CLu)::(CHexCh (HDe))::[]


let false_chars : Prims.list<ch> = (CHexCh (HDf))::(CHexCh (HDa))::(CPlain ("l"))::(CPlain ("s"))::(CHexCh (HDe))::[]


let canonical_float = (fun ( w  :  wire<'num, 'flt> ) ( f  :  'flt ) -> (match ((w.fclass f)) with
| FNaN -> begin
     (quoted nan_chars)
     end
| FPosInf -> begin
     (quoted inf_chars)
     end
| FNegInf -> begin
     (quoted neg_inf_chars)
     end
| FFinite -> begin
     (w.float_str ( 
if (w.is_zero f) then begin
     w.pos_zero
     end else begin
     f
     end))
     end))


let rec insert_kv = (fun ( w  :  wire<'num, 'flt> ) ( kv  :  (Prims.list<ch> * jval<'num, 'flt>) ) ( l  :  Prims.list<(Prims.list<ch> * jval<'num, 'flt>)> ) -> (match (l) with
| [] -> begin
     (kv)::[]
     end
| (hd)::t -> begin
      
if (w.key_le (FStar_Pervasives_Native.fst kv) (FStar_Pervasives_Native.fst hd)) then begin
     (kv)::l
     end else begin
     (hd)::(insert_kv w kv t)
     end
     end))


let rec sort_kvs = (fun ( w  :  wire<'num, 'flt> ) ( fs  :  Prims.list<(Prims.list<ch> * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     []
     end
| (kv)::t -> begin
     (insert_kv w kv (sort_kvs w t))
     end))


let rec render = (fun ( w  :  wire<'num, 'flt> ) ( v  :  jval<'num, 'flt> ) -> (match (v) with
| JStr (s) -> begin
     (quoted s)
     end
| JInt (i) -> begin
     (w.int_str i)
     end
| JBool (b) -> begin
      
if b then begin
     true_chars
     end else begin
     false_chars
     end
     end
| JFloat (f) -> begin
     (canonical_float w f)
     end
| JArr (xs) -> begin
     (CLBrack)::(render_items w xs)
     end
| JObj (fs) -> begin
     (CLBrace)::(render_kvs w (sort_kvs w fs))
     end))
and render_items = (fun ( w  :  wire<'num, 'flt> ) ( xs  :  Prims.list<jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     (CRBrack)::[]
     end
| (x)::[] -> begin
     (app (render w x) ((CRBrack)::[]))
     end
| (x)::t -> begin
     (app (render w x) ((CComma)::(render_items w t)))
     end))
and render_kvs = (fun ( w  :  wire<'num, 'flt> ) ( fs  :  Prims.list<(Prims.list<ch> * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     (CRBrace)::[]
     end
| ((k, v))::[] -> begin
     (app (quoted k) ((CColon)::(app (render w v) ((CRBrace)::[]))))
     end
| ((k, v))::t -> begin
     (app (quoted k) ((CColon)::(app (render w v) ((CComma)::(render_kvs w t)))))
     end))


let has_marker : Prims.list<ch>  ->  Prims.bool = (fun ( t  :  Prims.list<ch> ) -> ((mem CDot t) || (mem CUpE t)))


let float_canonical = (fun ( w  :  wire<'num, 'flt> ) ( f  :  'flt ) -> ((Prims.op_Equals (w.fclass f) FFinite) && (has_marker (w.float_str ( 
if (w.is_zero f) then begin
     w.pos_zero
     end else begin
     f
     end)))))


let rec canonical = (fun ( w  :  wire<'num, 'flt> ) ( v  :  jval<'num, 'flt> ) -> (match (v) with
| JFloat (f) -> begin
     (float_canonical w f)
     end
| JArr (xs) -> begin
     (canonical_items w xs)
     end
| JObj (fs) -> begin
     (canonical_kvs w fs)
     end
| uu___ -> begin
     true
     end))
and canonical_items = (fun ( w  :  wire<'num, 'flt> ) ( xs  :  Prims.list<jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((canonical w x) && (canonical_items w t))
     end))
and canonical_kvs = (fun ( w  :  wire<'num, 'flt> ) ( fs  :  Prims.list<(Prims.list<ch> * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     true
     end
| ((uu___, v))::t -> begin
     ((canonical w v) && (canonical_kvs w t))
     end))


let rec normalise = (fun ( w  :  wire<'num, 'flt> ) ( v  :  jval<'num, 'flt> ) -> (match (v) with
| JArr (xs) -> begin
     JArr ((normalise_items w xs))
     end
| JObj (fs) -> begin
     JObj ((normalise_kvs w (sort_kvs w fs)))
     end
| other -> begin
     other
     end))
and normalise_items = (fun ( w  :  wire<'num, 'flt> ) ( xs  :  Prims.list<jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((normalise w x))::(normalise_items w t)
     end))
and normalise_kvs = (fun ( w  :  wire<'num, 'flt> ) ( fs  :  Prims.list<(Prims.list<ch> * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     []
     end
| ((k, v))::t -> begin
     (((k), ((normalise w v))))::(normalise_kvs w t)
     end))


let num_ch : ch  ->  Prims.bool = (fun ( c  :  ch ) -> ((((((match (c) with
| CHexCh (d) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (is_dec (match (c) with
| CHexCh (d) -> begin
     d
     end))) || (Prims.op_Equals c CMinus)) || (Prims.op_Equals c CPlus)) || (Prims.op_Equals c CDot)) || (Prims.op_Equals c CUpE)))


let rec all_num : Prims.list<ch>  ->  Prims.bool = (fun ( t  :  Prims.list<ch> ) -> (match (t) with
| [] -> begin
     true
     end
| (c)::r -> begin
     ((num_ch c) && (all_num r))
     end))


let sep_ok : Prims.list<ch>  ->  Prims.bool = (fun ( rest  :  Prims.list<ch> ) -> (match (rest) with
| [] -> begin
     true
     end
| (c)::uu___ -> begin
     (not ((num_ch c)))
     end))


let rec span_num : Prims.list<ch>  ->  (Prims.list<ch> * Prims.list<ch>) = (fun ( input  :  Prims.list<ch> ) -> (match (input) with
| [] -> begin
     (([]), ([]))
     end
| (c)::t -> begin
      
if (num_ch c) then begin
     (

let uu___ = (span_num t)
in (match (uu___) with
| (tok, rest) -> begin
     (((c)::tok), (rest))
     end))
     end else begin
     (([]), (input))
     end
     end))


let rec strip : Prims.list<ch>  ->  Prims.list<ch>  ->  outcome<Prims.list<ch>> = (fun ( pfx  :  Prims.list<ch> ) ( input  :  Prims.list<ch> ) -> (match (pfx) with
| [] -> begin
     Ok (input)
     end
| (a)::pt -> begin
     (match (input) with
| [] -> begin
     Error ("input ended inside a literal")
     end
| (b)::it -> begin
      
if (Prims.op_Equals a b) then begin
     (strip pt it)
     end else begin
     Error ("not the literal this position expects")
     end
     end)
     end))


let ctrl_hi : hexd  ->  outcome<Prims.bool> = (fun ( d  :  hexd ) -> (match (d) with
| HD0 -> begin
     Ok (false)
     end
| HD1 -> begin
     Ok (true)
     end
| uu___ -> begin
     Error ("a u00xx escape whose high nibble is neither 0 nor 1 is not one this encoder emits")
     end))


let rec read_str : Prims.list<ch>  ->  outcome<(Prims.list<ch> * Prims.list<ch>)> = (fun ( input  :  Prims.list<ch> ) -> (match (input) with
| [] -> begin
     Error ("unterminated string")
     end
| (CQuote)::t -> begin
     Ok ((([]), (t)))
     end
| (CBackslash)::(CQuote)::t -> begin
     (match ((read_str t)) with
| Ok (s, rest) -> begin
     Ok ((((CQuote)::s), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| (CBackslash)::(CBackslash)::t -> begin
     (match ((read_str t)) with
| Ok (s, rest) -> begin
     Ok ((((CBackslash)::s), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| (CBackslash)::(CLu)::(CHexCh (HD0))::(CHexCh (HD0))::(CHexCh (h))::(CHexCh (l))::t -> begin
     (match ((ctrl_hi h)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (hi) -> begin
     (match ((read_str t)) with
| Ok (s, rest) -> begin
     Ok ((((CCtrl (hi, l))::s), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end)
     end
| (CBackslash)::uu___ -> begin
     Error ("not an escape this encoder emits")
     end
| (c)::t -> begin
     (match ((read_str t)) with
| Ok (s, rest) -> begin
     Ok ((((c)::s), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end))


let rec read = (fun ( w  :  wire<'num, 'flt> ) ( input  :  Prims.list<ch> ) -> (match (input) with
| [] -> begin
     Error ("no value here")
     end
| (CQuote)::t -> begin
     (match ((read_str t)) with
| Ok (s, rest) -> begin
     Ok (((JStr (s)), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| (CLBrack)::t -> begin
     (match ((read_items w t)) with
| Ok (xs, rest) -> begin
     Ok (((JArr (xs)), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| (CLBrace)::t -> begin
     (match ((read_kvs w t)) with
| Ok (fs, rest) -> begin
     Ok (((JObj (fs)), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| (CPlain (uu___))::uu___1 -> begin
     (match ((strip true_chars input)) with
| Ok (rest) -> begin
     Ok (((JBool (true)), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| (CHexCh (HDf))::uu___ -> begin
     (match ((strip false_chars input)) with
| Ok (rest) -> begin
     Ok (((JBool (false)), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| uu___ -> begin
     (

let uu___1 = (span_num input)
in (match (uu___1) with
| (tok, rest) -> begin
     (match (tok) with
| [] -> begin
     Error ("not the first character of any canonical value")
     end
| uu___2 -> begin
     (match ((w.tok_read tok)) with
| Ok (v) -> begin
     Ok (((v), (rest)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end)
     end))
     end))
and read_items = (fun ( w  :  wire<'num, 'flt> ) ( input  :  Prims.list<ch> ) -> (match (input) with
| (CRBrack)::t -> begin
     Ok ((([]), (t)))
     end
| uu___ -> begin
     (match ((read w input)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (v, r1) -> begin
     (match (r1) with
| (CComma)::r2 -> begin
     (match ((read_items w r2)) with
| Ok (vs, r3) -> begin
     Ok ((((v)::vs), (r3)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| (CRBrack)::r2 -> begin
     Ok ((((v)::[]), (r2)))
     end
| uu___1 -> begin
     Error ("expected a comma or a closing bracket")
     end)
     end)
     end))
and read_kvs = (fun ( w  :  wire<'num, 'flt> ) ( input  :  Prims.list<ch> ) -> (match (input) with
| (CRBrace)::t -> begin
     Ok ((([]), (t)))
     end
| (CQuote)::t -> begin
     (match ((read_str t)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (k, r1) -> begin
     (match (r1) with
| (CColon)::r2 -> begin
     (match ((read w r2)) with
| Error (m) -> begin
     Error (m)
     end
| Ok (v, r3) -> begin
     (match (r3) with
| (CComma)::r4 -> begin
     (match ((read_kvs w r4)) with
| Ok (kvs, r5) -> begin
     Ok ((((((k), (v)))::kvs), (r5)))
     end
| Error (m) -> begin
     Error (m)
     end)
     end
| (CRBrace)::r4 -> begin
     Ok ((((((k), (v)))::[]), (r4)))
     end
| uu___ -> begin
     Error ("expected a comma or a closing brace")
     end)
     end)
     end
| uu___ -> begin
     Error ("expected a colon after a member key")
     end)
     end)
     end
| uu___ -> begin
     Error ("expected a member key or a closing brace")
     end))


let numeric_token : Prims.list<ch>  ->  Prims.bool = (fun ( t  :  Prims.list<ch> ) -> ((match (t) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_num t)))


let rec sorted = (fun ( w  :  wire<'num, 'flt> ) ( fs  :  Prims.list<(Prims.list<ch> * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     true
     end
| ((k1, uu___))::t -> begin
     (match (t) with
| [] -> begin
     true
     end
| ((k2, uu___1))::uu___2 -> begin
     ((w.key_le k1 k2) && (sorted w t))
     end)
     end))


let rec keys_of = (fun ( fs  :  Prims.list<(Prims.list<ch> * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     []
     end
| ((k, uu___))::t -> begin
     (k)::(keys_of t)
     end))


let rec normal = (fun ( w  :  wire<'num, 'flt> ) ( v  :  jval<'num, 'flt> ) -> (match (v) with
| JArr (xs) -> begin
     (normal_items w xs)
     end
| JObj (fs) -> begin
     ((sorted w fs) && (normal_kvs w fs))
     end
| uu___ -> begin
     true
     end))
and normal_items = (fun ( w  :  wire<'num, 'flt> ) ( xs  :  Prims.list<jval<'num, 'flt>> ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((normal w x) && (normal_items w t))
     end))
and normal_kvs = (fun ( w  :  wire<'num, 'flt> ) ( fs  :  Prims.list<(Prims.list<ch> * jval<'num, 'flt>)> ) -> (match (fs) with
| [] -> begin
     true
     end
| ((uu___, v))::t -> begin
     ((normal w v) && (normal_kvs w t))
     end))




