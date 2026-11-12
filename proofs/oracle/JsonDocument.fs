module JsonDocument
type jdoc<'s, 't> =
| DNull
| DBool of Prims.bool
| DNum of 't
| DStr of 's
| DArr of Prims.list<jdoc<'s, 't>>
| DObj of Prims.list<('s * jdoc<'s, 't>)>


let uu___is_DNull = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DNull -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DBool = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DBool (b) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DBool__item__b = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DBool (b) -> begin
     b
     end))


let uu___is_DNum = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DNum (n) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DNum__item__n = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DNum (n) -> begin
     n
     end))


let uu___is_DStr = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DStr (str) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DStr__item__str = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DStr (str) -> begin
     str
     end))


let uu___is_DArr = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DArr (items) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DArr__item__items = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DArr (items) -> begin
     items
     end))


let uu___is_DObj = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DObj (fields) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DObj__item__fields = (fun ( projectee  :  jdoc<'s, 't> ) -> (match (projectee) with
| DObj (fields) -> begin
     fields
     end))


type pdoc = jdoc<Prims.list<JsonParse.och>, Prims.list<JsonParse.ch>>


let rec has_dkey : Prims.list<JsonParse.och>  ->  Prims.list<(Prims.list<JsonParse.och> * pdoc)>  ->  Prims.bool = (fun ( k  :  Prims.list<JsonParse.och> ) ( fs  :  Prims.list<(Prims.list<JsonParse.och> * pdoc)> ) -> (match (fs) with
| [] -> begin
     false
     end
| ((k', uu___))::t -> begin
     ((Prims.op_Equals k k') || (has_dkey k t))
     end))


let rec lossless_value : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  Prims.string  ->  JsonParse.lims  ->  Prims.list<unit>  ->  Prims.list<JsonParse.ch>  ->  JsonParse.pres<pdoc> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( cap  :  Prims.string ) ( lim  :  JsonParse.lims ) ( b  :  Prims.list<unit> ) ( s  :  Prims.list<JsonParse.ch> ) -> (

let w = (JsonParse.skip_ws s)
in (match (w) with
| [] -> begin
     JsonParse.PErr (JsonParse.UnexpectedEndOfInput, "unexpected end of input", [])
     end
| (JsonParse.CQuote)::uu___ -> begin
     (match ((JsonParse.parse_string lim.lstr w)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (cs, r) -> begin
     JsonParse.POk (DStr (cs), r)
     end)
     end
| (JsonParse.CLBrace)::uu___ -> begin
     (lossless_object fr cap lim b w)
     end
| (JsonParse.CLBrack)::uu___ -> begin
     (lossless_array fr cap lim b w)
     end
| (JsonParse.CLt)::(JsonParse.CLr)::(JsonParse.CLu)::(JsonParse.CLe)::t -> begin
     JsonParse.POk (DBool (true), t)
     end
| (JsonParse.CLt)::uu___ -> begin
     JsonParse.PErr (JsonParse.ExpectedToken, (JsonParse.msg_expect_lit "true"), w)
     end
| (JsonParse.CLf)::(JsonParse.CLa)::(JsonParse.CLl)::(JsonParse.CLs)::(JsonParse.CLe)::t -> begin
     JsonParse.POk (DBool (false), t)
     end
| (JsonParse.CLf)::uu___ -> begin
     JsonParse.PErr (JsonParse.ExpectedToken, (JsonParse.msg_expect_lit "false"), w)
     end
| (JsonParse.CLn)::(JsonParse.CLu)::(JsonParse.CLl)::(JsonParse.CLl)::t -> begin
     JsonParse.POk (DNull, t)
     end
| (JsonParse.CLn)::uu___ -> begin
     JsonParse.PErr (JsonParse.ExpectedToken, (JsonParse.msg_expect_lit "null"), w)
     end
| (c)::uu___ -> begin
      
if ((Prims.op_Equals c JsonParse.CMinus) || (JsonParse.is_digit c)) then begin
     (

let uu___1 = (JsonParse.scan_number w)
in (match (uu___1) with
| (tok, uu___2, rest) -> begin
      
if (JsonParse.is_json_number tok) then begin
     JsonParse.POk (DNum (tok), rest)
     end else begin
     JsonParse.PErr (JsonParse.MalformedNumber, (JsonParse.msg_malformed tok), rest)
     end
     end))
     end else begin
     JsonParse.PErr (JsonParse.UnexpectedChar, (JsonParse.msg_unexpected_char c), w)
     end
     end)))
and lossless_object : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  Prims.string  ->  JsonParse.lims  ->  Prims.list<unit>  ->  Prims.list<JsonParse.ch>  ->  JsonParse.pres<pdoc> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( cap  :  Prims.string ) ( lim  :  JsonParse.lims ) ( b  :  Prims.list<unit> ) ( w  :  Prims.list<JsonParse.ch> ) -> (match (b) with
| [] -> begin
     JsonParse.PErr (JsonParse.MaxDepthExceeded, (JsonParse.msg_depth cap), w)
     end
| (uu___)::b' -> begin
     (match ((JsonParse.expect JsonParse.CLBrace w)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (uu___1, t) -> begin
     (

let t1 = (JsonParse.skip_ws t)
in (match (t1) with
| (JsonParse.CRBrace)::t2 -> begin
     JsonParse.POk (DObj ([]), t2)
     end
| uu___2 -> begin
     (match ((lossless_members fr cap lim b' [] t1)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (fs, r) -> begin
     JsonParse.POk (DObj (fs), r)
     end)
     end))
     end)
     end))
and lossless_array : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  Prims.string  ->  JsonParse.lims  ->  Prims.list<unit>  ->  Prims.list<JsonParse.ch>  ->  JsonParse.pres<pdoc> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( cap  :  Prims.string ) ( lim  :  JsonParse.lims ) ( b  :  Prims.list<unit> ) ( w  :  Prims.list<JsonParse.ch> ) -> (match (b) with
| [] -> begin
     JsonParse.PErr (JsonParse.MaxDepthExceeded, (JsonParse.msg_depth cap), w)
     end
| (uu___)::b' -> begin
     (match ((JsonParse.expect JsonParse.CLBrack w)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (uu___1, t) -> begin
     (

let t1 = (JsonParse.skip_ws t)
in (match (t1) with
| (JsonParse.CRBrack)::t2 -> begin
     JsonParse.POk (DArr ([]), t2)
     end
| uu___2 -> begin
     (match ((lossless_items fr cap lim b' [] t1)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (xs, r) -> begin
     JsonParse.POk (DArr (xs), r)
     end)
     end))
     end)
     end))
and lossless_members : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  Prims.string  ->  JsonParse.lims  ->  Prims.list<unit>  ->  Prims.list<(Prims.list<JsonParse.och> * pdoc)>  ->  Prims.list<JsonParse.ch>  ->  JsonParse.pres<Prims.list<(Prims.list<JsonParse.och> * pdoc)>> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( cap  :  Prims.string ) ( lim  :  JsonParse.lims ) ( b  :  Prims.list<unit> ) ( acc  :  Prims.list<(Prims.list<JsonParse.och> * pdoc)> ) ( s  :  Prims.list<JsonParse.ch> ) -> (

let w = (JsonParse.skip_ws s)
in  
if (JsonParse.spent lim.lmembers acc) then begin
     JsonParse.PErr (JsonParse.LimitExceeded (JsonParse.LMembers), JsonParse.msg_limit, w)
     end else begin
     (match ((JsonParse.parse_string lim.lstr w)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (key, t) -> begin
     (

let t1 = (JsonParse.skip_ws t)
in (match ((JsonParse.expect JsonParse.CColon t1)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (uu___, t2) -> begin
     (

let t3 = (JsonParse.skip_ws t2)
in (match ((lossless_value fr cap lim b t3)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (v, t4) -> begin
      
if (has_dkey key acc) then begin
     JsonParse.PErr (JsonParse.DuplicateMember, JsonParse.msg_duplicate, w)
     end else begin
     (

let t5 = (JsonParse.skip_ws t4)
in (match (t5) with
| (JsonParse.CComma)::t6 -> begin
     (lossless_members fr cap lim b ((((key), (v)))::acc) t6)
     end
| (JsonParse.CRBrace)::t6 -> begin
     JsonParse.POk ((JsonParse.rev ((((key), (v)))::acc)), t6)
     end
| uu___1 -> begin
     JsonParse.PErr (JsonParse.ExpectedToken, "expected \',\' or \'}\'", t5)
     end))
     end
     end))
     end))
     end)
     end))
and lossless_items : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  Prims.string  ->  JsonParse.lims  ->  Prims.list<unit>  ->  Prims.list<pdoc>  ->  Prims.list<JsonParse.ch>  ->  JsonParse.pres<Prims.list<pdoc>> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( cap  :  Prims.string ) ( lim  :  JsonParse.lims ) ( b  :  Prims.list<unit> ) ( acc  :  Prims.list<pdoc> ) ( s  :  Prims.list<JsonParse.ch> ) ->  
if (JsonParse.spent lim.litems acc) then begin
     JsonParse.PErr (JsonParse.LimitExceeded (JsonParse.LItems), JsonParse.msg_limit, s)
     end else begin
     (match ((lossless_value fr cap lim b s)) with
| JsonParse.PErr (k, m, a) -> begin
     JsonParse.PErr (k, m, a)
     end
| JsonParse.POk (v, t) -> begin
     (

let t1 = (JsonParse.skip_ws t)
in (match (t1) with
| (JsonParse.CComma)::t2 -> begin
     (lossless_items fr cap lim b ((v)::acc) t2)
     end
| (JsonParse.CRBrack)::t2 -> begin
     JsonParse.POk ((JsonParse.rev ((v)::acc)), t2)
     end
| uu___ -> begin
     JsonParse.PErr (JsonParse.ExpectedToken, "expected \',\' or \']\'", t1)
     end))
     end)
     end)

type dresult =
| DOk of pdoc
| DErr of JsonParse.ekind * Prims.string * Prims.list<JsonParse.ch>
| DLimit of JsonParse.limit * Prims.list<JsonParse.ch>


let uu___is_DOk : dresult  ->  Prims.bool = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DOk (d) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DOk__item__d : dresult  ->  pdoc = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DOk (d) -> begin
     d
     end))


let uu___is_DErr : dresult  ->  Prims.bool = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DErr (k, msg, at) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DErr__item__k : dresult  ->  JsonParse.ekind = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DErr (k, msg, at) -> begin
     k
     end))


let __proj__DErr__item__msg : dresult  ->  Prims.string = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DErr (k, msg, at) -> begin
     msg
     end))


let __proj__DErr__item__at : dresult  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DErr (k, msg, at) -> begin
     at
     end))


let uu___is_DLimit : dresult  ->  Prims.bool = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DLimit (l, at) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DLimit__item__l : dresult  ->  JsonParse.limit = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DLimit (l, at) -> begin
     l
     end))


let __proj__DLimit__item__at : dresult  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  dresult ) -> (match (projectee) with
| DLimit (l, at) -> begin
     at
     end))


let read_lossless : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  Prims.string  ->  Prims.list<unit>  ->  JsonParse.lims  ->  Prims.bool  ->  Prims.list<JsonParse.ch>  ->  dresult = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( cap  :  Prims.string ) ( b  :  Prims.list<unit> ) ( lim  :  JsonParse.lims ) ( bytes_ok  :  Prims.bool ) ( s  :  Prims.list<JsonParse.ch> ) ->  
if (not (bytes_ok)) then begin
     DLimit (JsonParse.LBytes, s)
     end else begin
     (match ((lossless_value fr cap lim b s)) with
| JsonParse.PErr (JsonParse.MaxDepthExceeded, uu___, a) -> begin
     DLimit (JsonParse.LDepth, a)
     end
| JsonParse.PErr (JsonParse.LimitExceeded (l), uu___, a) -> begin
     DLimit (l, a)
     end
| JsonParse.PErr (k, m, a) -> begin
     DErr (k, m, a)
     end
| JsonParse.POk (d, r) -> begin
     (

let r1 = (JsonParse.skip_ws r)
in  
if (match (r1) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     DOk (d)
     end else begin
     DErr (JsonParse.TrailingCharacters, "trailing characters", r1)
     end)
     end)
     end)


let rec is_float_tok : Prims.list<JsonParse.ch>  ->  Prims.bool = (fun ( tok  :  Prims.list<JsonParse.ch> ) -> (match (tok) with
| [] -> begin
     false
     end
| (c)::t -> begin
     (((Prims.op_Equals c JsonParse.CDot) || (JsonParse.is_exp c)) || (is_float_tok t))
     end))


let rec to_jval : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  pdoc  ->  FStar_Pervasives_Native.option<JsonParse.jval> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( d  :  pdoc ) -> (match (d) with
| DNull -> begin
     FStar_Pervasives_Native.None
     end
| DBool (b) -> begin
     FStar_Pervasives_Native.Some (JsonParse.JBool (b))
     end
| DStr (cs) -> begin
     FStar_Pervasives_Native.Some (JsonParse.JStr (cs))
     end
| DNum (tok) -> begin
     (match ((JsonParse.classify_number fr tok (is_float_tok tok))) with
| JsonParse.POk (v, uu___) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| JsonParse.PErr (uu___, uu___1, uu___2) -> begin
     FStar_Pervasives_Native.None
     end)
     end
| DArr (xs) -> begin
     (match ((to_jvals fr xs)) with
| FStar_Pervasives_Native.Some (ys) -> begin
     FStar_Pervasives_Native.Some (JsonParse.JArr (ys))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| DObj (fs) -> begin
     (match ((to_fields fr fs)) with
| FStar_Pervasives_Native.Some (gs) -> begin
     FStar_Pervasives_Native.Some (JsonParse.JObj (gs))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end))
and to_jvals : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  Prims.list<pdoc>  ->  FStar_Pervasives_Native.option<Prims.list<JsonParse.jval>> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( xs  :  Prims.list<pdoc> ) -> (match (xs) with
| [] -> begin
     FStar_Pervasives_Native.Some ([])
     end
| (x)::t -> begin
     (match ((((to_jval fr x)), ((to_jvals fr t)))) with
| (FStar_Pervasives_Native.Some (y), FStar_Pervasives_Native.Some (ys)) -> begin
     FStar_Pervasives_Native.Some ((y)::ys)
     end
| (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end)
     end))
and to_fields : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  Prims.list<(Prims.list<JsonParse.och> * pdoc)>  ->  FStar_Pervasives_Native.option<Prims.list<(Prims.list<JsonParse.och> * JsonParse.jval)>> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( fs  :  Prims.list<(Prims.list<JsonParse.och> * pdoc)> ) -> (match (fs) with
| [] -> begin
     FStar_Pervasives_Native.Some ([])
     end
| ((k, x))::t -> begin
     (match ((((to_jval fr x)), ((to_fields fr t)))) with
| (FStar_Pervasives_Native.Some (y), FStar_Pervasives_Native.Some (ys)) -> begin
     FStar_Pervasives_Native.Some ((((k), (y)))::ys)
     end
| (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end)
     end))


let converted : (Prims.list<JsonParse.ch>  ->  JsonParse.freadv)  ->  dresult  ->  FStar_Pervasives_Native.option<JsonParse.jval> = (fun ( fr  :  Prims.list<JsonParse.ch>  ->  JsonParse.freadv ) ( r  :  dresult ) -> (match (r) with
| DOk (d) -> begin
     (to_jval fr d)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


type numval<'num, 'flt> = WireDecode.jval<'num, 'flt>


type ddoc<'t> = jdoc<Prims.string, 't>


let rec lower = (fun ( nd  :  't  ->  FStar_Pervasives_Native.option<numval<'num, 'flt>> ) ( d  :  ddoc<'t> ) -> (match (d) with
| DNull -> begin
     DecodeLayer.Error ((DecodeLayer.make DecodeLayer.NotAdmitted))
     end
| DBool (b) -> begin
     DecodeLayer.Ok (WireDecode.JBool (b))
     end
| DStr (s) -> begin
     DecodeLayer.Ok (WireDecode.JStr (s))
     end
| DNum (n) -> begin
     (match ((nd n)) with
| FStar_Pervasives_Native.Some (v) -> begin
     DecodeLayer.Ok (v)
     end
| FStar_Pervasives_Native.None -> begin
     DecodeLayer.Error ((DecodeLayer.make DecodeLayer.OutOfRange))
     end)
     end
| DArr (xs) -> begin
     (match ((lower_items nd (Prims.parse_int "0") xs)) with
| DecodeLayer.Ok (ys) -> begin
     DecodeLayer.Ok (WireDecode.JArr (ys))
     end
| DecodeLayer.Error (e) -> begin
     DecodeLayer.Error (e)
     end)
     end
| DObj (fs) -> begin
     (match ((lower_fields nd [] fs)) with
| DecodeLayer.Ok (gs) -> begin
     DecodeLayer.Ok (WireDecode.JObj (gs))
     end
| DecodeLayer.Error (e) -> begin
     DecodeLayer.Error (e)
     end)
     end))
and lower_items = (fun ( nd  :  't  ->  FStar_Pervasives_Native.option<numval<'num, 'flt>> ) ( i  :  Prims.int ) ( xs  :  Prims.list<ddoc<'t>> ) -> (match (xs) with
| [] -> begin
     DecodeLayer.Ok ([])
     end
| (x)::rest -> begin
     (match ((lower nd x)) with
| DecodeLayer.Error (e) -> begin
     DecodeLayer.Error ((DecodeLayer.under (DecodeLayer.Index (i)) e))
     end
| DecodeLayer.Ok (v) -> begin
     (match ((lower_items nd (i + (Prims.parse_int "1")) rest)) with
| DecodeLayer.Ok (vs) -> begin
     DecodeLayer.Ok ((v)::vs)
     end
| DecodeLayer.Error (e) -> begin
     DecodeLayer.Error (e)
     end)
     end)
     end))
and lower_fields = (fun ( nd  :  't  ->  FStar_Pervasives_Native.option<numval<'num, 'flt>> ) ( seen  :  Prims.list<Prims.string> ) ( fs  :  Prims.list<(Prims.string * ddoc<'t>)> ) -> (match (fs) with
| [] -> begin
     DecodeLayer.Ok ([])
     end
| ((k, x))::rest -> begin
      
if (ListAlgebra.mem k seen) then begin
     DecodeLayer.Error ((DecodeLayer.under (DecodeLayer.Key (k)) (DecodeLayer.make DecodeLayer.NotAdmitted)))
     end else begin
     (match ((lower nd x)) with
| DecodeLayer.Error (e) -> begin
     DecodeLayer.Error ((DecodeLayer.under (DecodeLayer.Key (k)) e))
     end
| DecodeLayer.Ok (v) -> begin
     (match ((lower_fields nd ((k)::seen) rest)) with
| DecodeLayer.Ok (vs) -> begin
     DecodeLayer.Ok ((((k), (v)))::vs)
     end
| DecodeLayer.Error (e) -> begin
     DecodeLayer.Error (e)
     end)
     end)
     end
     end))


let of_decoder = (fun ( nd  :  't  ->  FStar_Pervasives_Native.option<numval<'num, 'flt>> ) ( dc  :  DecodeLayer.dec<'num, 'flt, 'a> ) ( x  :  ddoc<'t> ) -> (match ((lower nd x)) with
| DecodeLayer.Error (e) -> begin
     DecodeLayer.Error (e)
     end
| DecodeLayer.Ok (v) -> begin
     (dc v)
     end))


let rec dmemb = (fun ( k  :  Prims.string ) ( fs  :  Prims.list<(Prims.string * ddoc<'t>)> ) -> (match (fs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, v))::rest -> begin
      
if (Prims.op_Equals n k) then begin
     FStar_Pervasives_Native.Some (v)
     end else begin
     (dmemb k rest)
     end
     end))


let rec dhas_key = (fun ( k  :  Prims.string ) ( fs  :  Prims.list<(Prims.string * ddoc<'t>)> ) -> (match (fs) with
| [] -> begin
     false
     end
| ((n, uu___))::rest -> begin
     ((Prims.op_Equals n k) || (dhas_key k rest))
     end))


let dstep = (fun ( s  :  DecodeLayer.seg ) ( d  :  ddoc<'t> ) -> (match (((s), (d))) with
| (DecodeLayer.Key (k), DObj (fs)) -> begin
     (dmemb k fs)
     end
| (DecodeLayer.Index (i), DArr (xs)) -> begin
      
if (i >= (Prims.parse_int "0")) then begin
     (DecodeLayer.try_item i xs)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end))


let rec dresolve = (fun ( p  :  Prims.list<DecodeLayer.seg> ) ( d  :  ddoc<'t> ) -> (match (p) with
| [] -> begin
     FStar_Pervasives_Native.Some (d)
     end
| (s)::rest -> begin
     (match ((dstep s d)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (dresolve rest v)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end))


let d_resolves_in = (fun ( d  :  ddoc<'t> ) ( e  :  DecodeLayer.derr ) -> (match (e.code) with
| DecodeLayer.InvalidJson -> begin
     (match (e.path) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| DecodeLayer.MissingField -> begin
     (match ((WireDecode.rev e.path)) with
| (DecodeLayer.Key (k))::parent_rev -> begin
     (match ((dresolve (WireDecode.rev parent_rev) d)) with
| FStar_Pervasives_Native.Some (DObj (fields)) -> begin
     (not ((dhas_key k fields)))
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
     (match ((dresolve e.path d)) with
| FStar_Pervasives_Native.Some (uu___1) -> begin
     true
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)
     end))


let null_doc : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CLn)::(JsonParse.CLu)::(JsonParse.CLl)::(JsonParse.CLl)::(JsonParse.CComma)::(JsonParse.CQuote)::(JsonParse.CLb)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CMinus)::(JsonParse.CD1)::(JsonParse.CD2)::(JsonParse.CDot)::(JsonParse.CD5)::(JsonParse.CD0)::(JsonParse.CRBrace)::[]


let null_doc_read : dresult = (read_lossless (fun ( uu___  :  Prims.list<JsonParse.ch> ) -> JsonParse.FFinite) "c" ((())::(())::[]) JsonParse.unbounded true null_doc)


let null_doc_reads_as_written : Prims.bool = (Prims.op_Equals null_doc_read (DOk (DObj (((((JsonParse.OLit (JsonParse.CLa))::[]), (DNull)))::((((JsonParse.OLit (JsonParse.CLb))::[]), (DNum ((JsonParse.CMinus)::(JsonParse.CD1)::(JsonParse.CD2)::(JsonParse.CDot)::(JsonParse.CD5)::(JsonParse.CD0)::[]))))::[]))))


let null_doc_converts_to_nothing : Prims.bool = (match ((converted (fun ( uu___  :  Prims.list<JsonParse.ch> ) -> JsonParse.FFinite) null_doc_read)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end)


let num_doc : Prims.list<JsonParse.ch> = (JsonParse.CLBrack)::(JsonParse.CMinus)::(JsonParse.CD1)::(JsonParse.CD2)::(JsonParse.CDot)::(JsonParse.CD5)::(JsonParse.CD0)::(JsonParse.CRBrack)::[]


let num_doc_agrees : Prims.bool = (Prims.op_Equals (converted (fun ( uu___  :  Prims.list<JsonParse.ch> ) -> JsonParse.FFinite) (read_lossless (fun ( uu___  :  Prims.list<JsonParse.ch> ) -> JsonParse.FFinite) "c" ((())::[]) JsonParse.unbounded true num_doc)) (FStar_Pervasives_Native.Some (JsonParse.JArr ((JsonParse.JFloat ((JsonParse.CMinus)::(JsonParse.CD1)::(JsonParse.CD2)::(JsonParse.CDot)::(JsonParse.CD5)::(JsonParse.CD0)::[]))::[]))))


let lower_refuses_the_null : Prims.bool = (match ((lower (fun ( uu___  :  Prims.string ) -> FStar_Pervasives_Native.None) (DArr ((DBool (true))::(DNull)::[])))) with
| DecodeLayer.Error (e) -> begin
     ((Prims.op_Equals e.code DecodeLayer.NotAdmitted) && (Prims.op_Equals e.path ((DecodeLayer.Index ((Prims.parse_int "1")))::[])))
     end
| DecodeLayer.Ok (uu___) -> begin
     false
     end)


let twins : Prims.list<Twin.twin> = ({Twin.tname = "the-lossless-reading-keeps-a-null-and-a-number-as-written"; Twin.tholds = (fun ( uu___  :  unit ) -> null_doc_reads_as_written)})::({Twin.tname = "a-document-with-a-null-does-not-convert"; Twin.tholds = (fun ( uu___  :  unit ) -> null_doc_converts_to_nothing)})::({Twin.tname = "a-converting-document-is-the-parse"; Twin.tholds = (fun ( uu___  :  unit ) -> num_doc_agrees)})::({Twin.tname = "the-float-flag-reads-the-exponent"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (is_float_tok ((JsonParse.CD1)::(JsonParse.CUe)::(JsonParse.CD2)::[])) true))})::({Twin.tname = "the-float-flag-of-an-integer-is-false"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (is_float_tok ((JsonParse.CMinus)::(JsonParse.CD1)::(JsonParse.CD2)::[])) false))})::({Twin.tname = "lower-refuses-a-null-not-admitted"; Twin.tholds = (fun ( uu___  :  unit ) -> lower_refuses_the_null)})::[]




