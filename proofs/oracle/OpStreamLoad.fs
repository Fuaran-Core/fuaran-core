module OpStreamLoad
type res<'a, 'e> =
| Ok of 'a
| Error of 'e


let uu___is_Ok = (fun ( projectee  :  res<'a, 'e> ) -> (match (projectee) with
| Ok (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ok__item__v = (fun ( projectee  :  res<'a, 'e> ) -> (match (projectee) with
| Ok (v) -> begin
     v
     end))


let uu___is_Error = (fun ( projectee  :  res<'a, 'e> ) -> (match (projectee) with
| Error (err) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Error__item__err = (fun ( projectee  :  res<'a, 'e> ) -> (match (projectee) with
| Error (err) -> begin
     err
     end))


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let sub : Prims.nat  ->  Prims.nat  ->  Prims.nat = (fun ( a  :  Prims.nat ) ( b  :  Prims.nat ) ->  
if (b <= a) then begin
     (a - b)
     end else begin
     (Prims.parse_int "0")
     end)


let pos_in : Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch>  ->  Prims.nat = (fun ( whole  :  Prims.list<JsonParse.ch> ) ( s  :  Prims.list<JsonParse.ch> ) -> (sub (len whole) (len s)))


let rec take_n : Prims.nat  ->  Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch> = (fun ( n  :  Prims.nat ) ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| [] -> begin
     []
     end
| (c)::t -> begin
      
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     []
     end else begin
     (c)::(take_n (n - (Prims.parse_int "1")) t)
     end
     end))


let rec all = (fun ( p  :  'a  ->  Prims.bool ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((p x) && (all p t))
     end))


let rec map = (fun ( f  :  'a  ->  'b ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((f x))::(map f t)
     end))


let rec join_sep : JsonParse.ch  ->  Prims.list<Prims.list<JsonParse.ch>>  ->  Prims.list<JsonParse.ch> = (fun ( sep  :  JsonParse.ch ) ( ls  :  Prims.list<Prims.list<JsonParse.ch>> ) -> (match (ls) with
| [] -> begin
     []
     end
| (l)::[] -> begin
     l
     end
| (l)::rest -> begin
     (JsonParse.app l ((sep)::(join_sep sep rest)))
     end))


let rec olit : Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.och> = (fun ( k  :  Prims.list<JsonParse.ch> ) -> (match (k) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (JsonParse.OLit (c))::(olit t)
     end))

type seg =
| SKey of Prims.list<JsonParse.och>
| SIdx of Prims.nat


let uu___is_SKey : seg  ->  Prims.bool = (fun ( projectee  :  seg ) -> (match (projectee) with
| SKey (k) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SKey__item__k : seg  ->  Prims.list<JsonParse.och> = (fun ( projectee  :  seg ) -> (match (projectee) with
| SKey (k) -> begin
     k
     end))


let uu___is_SIdx : seg  ->  Prims.bool = (fun ( projectee  :  seg ) -> (match (projectee) with
| SIdx (i) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SIdx__item__i : seg  ->  Prims.nat = (fun ( projectee  :  seg ) -> (match (projectee) with
| SIdx (i) -> begin
     i
     end))

type reason =
| NotAnObject
| Truncated
| ExpectedKey
| ExpectedColon
| MissingValue
| ExpectedCommaOrBrace
| TrailingContent
| UnterminatedString
| UnterminatedContainer
| InvalidEscape of Prims.list<JsonParse.ch>
| InvalidLiteral of Prims.list<JsonParse.ch>
| ExpectedString of Prims.list<JsonParse.ch>
| ExpectedInteger of Prims.list<JsonParse.ch>
| ExpectedStringArray of Prims.list<JsonParse.ch>
| MissingField of Prims.list<JsonParse.ch>
| SnapshotNotAtHead
| Refused of Prims.string
| RefusedNoKind
| RefusedKind of Prims.list<JsonParse.och>
| ActorInvalid of Prims.list<JsonParse.ch>
| ExpectedCommaOrBracket
| RepeatedKey of Prims.list<seg>
| RawControl of Prims.list<seg>
| LoneSurrogate of Prims.list<seg>


let uu___is_NotAnObject : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| NotAnObject -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Truncated : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| Truncated -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ExpectedKey : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedKey -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ExpectedColon : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedColon -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_MissingValue : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| MissingValue -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ExpectedCommaOrBrace : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedCommaOrBrace -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TrailingContent : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| TrailingContent -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UnterminatedString : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| UnterminatedString -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UnterminatedContainer : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| UnterminatedContainer -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_InvalidEscape : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| InvalidEscape (escape) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__InvalidEscape__item__escape : reason  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  reason ) -> (match (projectee) with
| InvalidEscape (escape) -> begin
     escape
     end))


let uu___is_InvalidLiteral : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| InvalidLiteral (token) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__InvalidLiteral__item__token : reason  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  reason ) -> (match (projectee) with
| InvalidLiteral (token) -> begin
     token
     end))


let uu___is_ExpectedString : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedString (field) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ExpectedString__item__field : reason  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedString (field) -> begin
     field
     end))


let uu___is_ExpectedInteger : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedInteger (field) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ExpectedInteger__item__field : reason  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedInteger (field) -> begin
     field
     end))


let uu___is_ExpectedStringArray : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedStringArray (field) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ExpectedStringArray__item__field : reason  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedStringArray (field) -> begin
     field
     end))


let uu___is_MissingField : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| MissingField (field) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MissingField__item__field : reason  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  reason ) -> (match (projectee) with
| MissingField (field) -> begin
     field
     end))


let uu___is_SnapshotNotAtHead : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| SnapshotNotAtHead -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Refused : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| Refused (why) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Refused__item__why : reason  ->  Prims.string = (fun ( projectee  :  reason ) -> (match (projectee) with
| Refused (why) -> begin
     why
     end))


let uu___is_RefusedNoKind : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| RefusedNoKind -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_RefusedKind : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| RefusedKind (kind) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RefusedKind__item__kind : reason  ->  Prims.list<JsonParse.och> = (fun ( projectee  :  reason ) -> (match (projectee) with
| RefusedKind (kind) -> begin
     kind
     end))


let uu___is_ActorInvalid : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| ActorInvalid (field) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ActorInvalid__item__field : reason  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  reason ) -> (match (projectee) with
| ActorInvalid (field) -> begin
     field
     end))


let uu___is_ExpectedCommaOrBracket : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| ExpectedCommaOrBracket -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_RepeatedKey : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| RepeatedKey (path) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RepeatedKey__item__path : reason  ->  Prims.list<seg> = (fun ( projectee  :  reason ) -> (match (projectee) with
| RepeatedKey (path) -> begin
     path
     end))


let uu___is_RawControl : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| RawControl (path) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RawControl__item__path : reason  ->  Prims.list<seg> = (fun ( projectee  :  reason ) -> (match (projectee) with
| RawControl (path) -> begin
     path
     end))


let uu___is_LoneSurrogate : reason  ->  Prims.bool = (fun ( projectee  :  reason ) -> (match (projectee) with
| LoneSurrogate (path) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LoneSurrogate__item__path : reason  ->  Prims.list<seg> = (fun ( projectee  :  reason ) -> (match (projectee) with
| LoneSurrogate (path) -> begin
     path
     end))

type fault = {fline : Prims.nat; fpos : Prims.nat; freason : reason}


let __proj__Mkfault__item__fline : fault  ->  Prims.nat = (fun ( projectee  :  fault ) -> (match (projectee) with
| {fline = fline; fpos = fpos; freason = freason} -> begin
     fline
     end))


let __proj__Mkfault__item__fpos : fault  ->  Prims.nat = (fun ( projectee  :  fault ) -> (match (projectee) with
| {fline = fline; fpos = fpos; freason = freason} -> begin
     fpos
     end))


let __proj__Mkfault__item__freason : fault  ->  reason = (fun ( projectee  :  fault ) -> (match (projectee) with
| {fline = fline; fpos = fpos; freason = freason} -> begin
     freason
     end))


let fault_at : Prims.nat  ->  Prims.nat  ->  reason  ->  fault = (fun ( line  :  Prims.nat ) ( pos  :  Prims.nat ) ( r  :  reason ) -> {fline = line; fpos = pos; freason = r})

type wreason =
| MultiLine of Prims.nat
| NotTrimStable
| Unreadable of reason


let uu___is_MultiLine : wreason  ->  Prims.bool = (fun ( projectee  :  wreason ) -> (match (projectee) with
| MultiLine (position) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MultiLine__item__position : wreason  ->  Prims.nat = (fun ( projectee  :  wreason ) -> (match (projectee) with
| MultiLine (position) -> begin
     position
     end))


let uu___is_NotTrimStable : wreason  ->  Prims.bool = (fun ( projectee  :  wreason ) -> (match (projectee) with
| NotTrimStable -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Unreadable : wreason  ->  Prims.bool = (fun ( projectee  :  wreason ) -> (match (projectee) with
| Unreadable (r) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Unreadable__item__r : wreason  ->  reason = (fun ( projectee  :  wreason ) -> (match (projectee) with
| Unreadable (r) -> begin
     r
     end))

type wfault = {wline : Prims.nat; wmember : Prims.list<JsonParse.ch>; wreason : wreason}


let __proj__Mkwfault__item__wline : wfault  ->  Prims.nat = (fun ( projectee  :  wfault ) -> (match (projectee) with
| {wline = wline; wmember = wmember; wreason = wreason1} -> begin
     wline
     end))


let __proj__Mkwfault__item__wmember : wfault  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  wfault ) -> (match (projectee) with
| {wline = wline; wmember = wmember; wreason = wreason1} -> begin
     wmember
     end))


let __proj__Mkwfault__item__wreason : wfault  ->  wreason = (fun ( projectee  :  wfault ) -> (match (projectee) with
| {wline = wline; wmember = wmember; wreason = wreason1} -> begin
     wreason1
     end))

type usite = {uline : Prims.nat; unode : Prims.list<JsonParse.och>; ureason : Prims.string}


let __proj__Mkusite__item__uline : usite  ->  Prims.nat = (fun ( projectee  :  usite ) -> (match (projectee) with
| {uline = uline; unode = unode; ureason = ureason} -> begin
     uline
     end))


let __proj__Mkusite__item__unode : usite  ->  Prims.list<JsonParse.och> = (fun ( projectee  :  usite ) -> (match (projectee) with
| {uline = uline; unode = unode; ureason = ureason} -> begin
     unode
     end))


let __proj__Mkusite__item__ureason : usite  ->  Prims.string = (fun ( projectee  :  usite ) -> (match (projectee) with
| {uline = uline; unode = unode; ureason = ureason} -> begin
     ureason
     end))

type lfault<'brk> =
| LUnreadable of fault
| LBroken of 'brk
| LUndecodable of Prims.list<usite>


let uu___is_LUnreadable = (fun ( projectee  :  lfault<'brk> ) -> (match (projectee) with
| LUnreadable (f) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LUnreadable__item__f = (fun ( projectee  :  lfault<'brk> ) -> (match (projectee) with
| LUnreadable (f) -> begin
     f
     end))


let uu___is_LBroken = (fun ( projectee  :  lfault<'brk> ) -> (match (projectee) with
| LBroken (b) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LBroken__item__b = (fun ( projectee  :  lfault<'brk> ) -> (match (projectee) with
| LBroken (b) -> begin
     b
     end))


let uu___is_LUndecodable = (fun ( projectee  :  lfault<'brk> ) -> (match (projectee) with
| LUndecodable (sites) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LUndecodable__item__sites = (fun ( projectee  :  lfault<'brk> ) -> (match (projectee) with
| LUndecodable (sites) -> begin
     sites
     end))

type sres<'a> =
| SOk of 'a * Prims.list<JsonParse.ch>
| SErr of reason * Prims.list<JsonParse.ch>


let uu___is_SOk = (fun ( projectee  :  sres<'a> ) -> (match (projectee) with
| SOk (v, rest) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SOk__item__v = (fun ( projectee  :  sres<'a> ) -> (match (projectee) with
| SOk (v, rest) -> begin
     v
     end))


let __proj__SOk__item__rest = (fun ( projectee  :  sres<'a> ) -> (match (projectee) with
| SOk (v, rest) -> begin
     rest
     end))


let uu___is_SErr = (fun ( projectee  :  sres<'a> ) -> (match (projectee) with
| SErr (why, at) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SErr__item__why = (fun ( projectee  :  sres<'a> ) -> (match (projectee) with
| SErr (why, at) -> begin
     why
     end))


let __proj__SErr__item__at = (fun ( projectee  :  sres<'a> ) -> (match (projectee) with
| SErr (why, at) -> begin
     at
     end))

type hex4r =
| H4 of JsonParse.ch * JsonParse.ch * JsonParse.ch * JsonParse.ch * Prims.list<JsonParse.ch>
| NoHex4


let uu___is_H4 : hex4r  ->  Prims.bool = (fun ( projectee  :  hex4r ) -> (match (projectee) with
| H4 (a, b, c, d, rest) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__H4__item__a : hex4r  ->  JsonParse.ch = (fun ( projectee  :  hex4r ) -> (match (projectee) with
| H4 (a, b, c, d, rest) -> begin
     a
     end))


let __proj__H4__item__b : hex4r  ->  JsonParse.ch = (fun ( projectee  :  hex4r ) -> (match (projectee) with
| H4 (a, b, c, d, rest) -> begin
     b
     end))


let __proj__H4__item__c : hex4r  ->  JsonParse.ch = (fun ( projectee  :  hex4r ) -> (match (projectee) with
| H4 (a, b, c, d, rest) -> begin
     c
     end))


let __proj__H4__item__d : hex4r  ->  JsonParse.ch = (fun ( projectee  :  hex4r ) -> (match (projectee) with
| H4 (a, b, c, d, rest) -> begin
     d
     end))


let __proj__H4__item__rest : hex4r  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  hex4r ) -> (match (projectee) with
| H4 (a, b, c, d, rest) -> begin
     rest
     end))


let uu___is_NoHex4 : hex4r  ->  Prims.bool = (fun ( projectee  :  hex4r ) -> (match (projectee) with
| NoHex4 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let hex4 : Prims.list<JsonParse.ch>  ->  hex4r = (fun ( u  :  Prims.list<JsonParse.ch> ) -> (match (u) with
| (a)::(b)::(c)::(d)::r -> begin
      
if ((((JsonParse.is_hex a) && (JsonParse.is_hex b)) && (JsonParse.is_hex c)) && (JsonParse.is_hex d)) then begin
     H4 (a, b, c, d, r)
     end else begin
     NoHex4
     end
     end
| uu___ -> begin
     NoHex4
     end))


let rec string_tail : Prims.list<seg>  ->  Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.och>  ->  Prims.list<JsonParse.ch>  ->  sres<Prims.list<JsonParse.och>> = (fun ( pth  :  Prims.list<seg> ) ( start  :  Prims.list<JsonParse.ch> ) ( acc  :  Prims.list<JsonParse.och> ) ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| [] -> begin
     SErr (UnterminatedString, start)
     end
| (JsonParse.CQuote)::t -> begin
     SOk ((JsonParse.rev acc), t)
     end
| (JsonParse.CBackslash)::t -> begin
     (match (t) with
| [] -> begin
     SErr (UnterminatedString, start)
     end
| (JsonParse.CLu)::u -> begin
     (match ((hex4 u)) with
| NoHex4 -> begin
     SErr (InvalidEscape ((take_n (Prims.parse_int "6") s)), s)
     end
| H4 (a, b, c, d, r) -> begin
     (match ((JsonParse.uni_class a b)) with
| JsonParse.SNone -> begin
     (string_tail pth start ((JsonParse.OUni (a, b, c, d))::acc) r)
     end
| JsonParse.SLow -> begin
     SErr (InvalidEscape ((take_n (Prims.parse_int "6") s)), s)
     end
| JsonParse.SHigh -> begin
     (match (r) with
| (JsonParse.CBackslash)::(JsonParse.CLu)::u2 -> begin
     (match ((hex4 u2)) with
| H4 (a2, b2, c2, d2, r2) -> begin
      
if (Prims.op_Equals (JsonParse.uni_class a2 b2) JsonParse.SLow) then begin
     (string_tail pth start ((JsonParse.OUni (a2, b2, c2, d2))::(JsonParse.OUni (a, b, c, d))::acc) r2)
     end else begin
     SErr (InvalidEscape ((take_n (Prims.parse_int "12") s)), s)
     end
     end
| NoHex4 -> begin
     SErr (InvalidEscape ((take_n (Prims.parse_int "12") s)), s)
     end)
     end
| (JsonParse.CLoSur (y))::r2 -> begin
     (string_tail pth start ((JsonParse.OLit (JsonParse.CLoSur (y)))::(JsonParse.OUni (a, b, c, d))::acc) r2)
     end
| uu___ -> begin
     SErr (InvalidEscape ((take_n (Prims.parse_int "12") s)), s)
     end)
     end)
     end)
     end
| (e)::u -> begin
      
if (JsonParse.is_short_escape e) then begin
     (string_tail pth start ((JsonParse.OEsc (e))::acc) u)
     end else begin
     SErr (InvalidEscape ((JsonParse.CBackslash)::(e)::[]), s)
     end
     end)
     end
| (JsonParse.CHiSur (x))::t -> begin
     (match (t) with
| [] -> begin
     SErr (UnterminatedString, start)
     end
| (JsonParse.CLoSur (y))::r -> begin
     (string_tail pth start ((JsonParse.OLit (JsonParse.CLoSur (y)))::(JsonParse.OLit (JsonParse.CHiSur (x)))::acc) r)
     end
| (JsonParse.CBackslash)::t1 -> begin
     (match (t1) with
| [] -> begin
     SErr (UnterminatedString, start)
     end
| (JsonParse.CLu)::u -> begin
     (match ((hex4 u)) with
| NoHex4 -> begin
     SErr (InvalidEscape ((take_n (Prims.parse_int "6") t)), t)
     end
| H4 (a, b, c, d, r) -> begin
      
if (Prims.op_Equals (JsonParse.uni_class a b) JsonParse.SLow) then begin
     (string_tail pth start ((JsonParse.OUni (a, b, c, d))::(JsonParse.OLit (JsonParse.CHiSur (x)))::acc) r)
     end else begin
     SErr (LoneSurrogate (pth), s)
     end
     end)
     end
| (e)::uu___ -> begin
      
if (JsonParse.is_short_escape e) then begin
     SErr (LoneSurrogate (pth), s)
     end else begin
     SErr (InvalidEscape ((JsonParse.CBackslash)::(e)::[]), t)
     end
     end)
     end
| (c)::uu___ -> begin
      
if (JsonParse.is_ctrl c) then begin
     SErr (RawControl (pth), t)
     end else begin
     SErr (LoneSurrogate (pth), s)
     end
     end)
     end
| (JsonParse.CLoSur (uu___))::uu___1 -> begin
     SErr (LoneSurrogate (pth), s)
     end
| (c)::t -> begin
      
if (JsonParse.is_ctrl c) then begin
     SErr (RawControl (pth), s)
     end else begin
     (string_tail pth start ((JsonParse.OLit (c))::acc) t)
     end
     end))


let lit_true : Prims.list<JsonParse.ch> = (JsonParse.CLt)::(JsonParse.CLr)::(JsonParse.CLu)::(JsonParse.CLe)::[]


let lit_false : Prims.list<JsonParse.ch> = (JsonParse.CLf)::(JsonParse.CLa)::(JsonParse.CLl)::(JsonParse.CLs)::(JsonParse.CLe)::[]


let lit_null : Prims.list<JsonParse.ch> = (JsonParse.CLn)::(JsonParse.CLu)::(JsonParse.CLl)::(JsonParse.CLl)::[]


let is_literal : Prims.list<JsonParse.ch>  ->  Prims.bool = (fun ( t  :  Prims.list<JsonParse.ch> ) -> ((((Prims.op_Equals t lit_true) || (Prims.op_Equals t lit_false)) || (Prims.op_Equals t lit_null)) || (JsonParse.is_json_number t)))


let is_terminator : JsonParse.ch  ->  Prims.bool = (fun ( c  :  JsonParse.ch ) -> ((((Prims.op_Equals c JsonParse.CComma) || (Prims.op_Equals c JsonParse.CRBrace)) || (Prims.op_Equals c JsonParse.CRBrack)) || (JsonParse.is_ws c)))


let rec bare_token : Prims.list<JsonParse.ch>  ->  (Prims.list<JsonParse.ch> * Prims.list<JsonParse.ch>) = (fun ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| [] -> begin
     (([]), ([]))
     end
| (c)::t -> begin
      
if (is_terminator c) then begin
     (([]), (s))
     end else begin
     (

let uu___ = (bare_token t)
in (match (uu___) with
| (tok, r) -> begin
     (((c)::tok), (r))
     end))
     end
     end))


let rec mem_key : Prims.list<JsonParse.och>  ->  Prims.list<Prims.list<JsonParse.och>>  ->  Prims.bool = (fun ( k  :  Prims.list<JsonParse.och> ) ( seen  :  Prims.list<Prims.list<JsonParse.och>> ) -> (match (seen) with
| [] -> begin
     false
     end
| (x)::t -> begin
     ((Prims.op_Equals k x) || (mem_key k t))
     end))


let rec skip_val : Prims.list<seg>  ->  Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch>  ->  sres<unit> = (fun ( pth  :  Prims.list<seg> ) ( start  :  Prims.list<JsonParse.ch> ) ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| [] -> begin
     SErr (Truncated, [])
     end
| (JsonParse.CQuote)::t -> begin
     (match ((string_tail pth s [] t)) with
| SErr (why, at) -> begin
     SErr (why, at)
     end
| SOk (uu___, r) -> begin
     SOk ((), r)
     end)
     end
| (JsonParse.CLBrace)::t -> begin
     (obj_members pth start true [] t)
     end
| (JsonParse.CLBrack)::t -> begin
     (arr_items pth start (Prims.parse_int "0") true t)
     end
| (JsonParse.CComma)::uu___ -> begin
     SErr (MissingValue, s)
     end
| (JsonParse.CRBrace)::uu___ -> begin
     SErr (MissingValue, s)
     end
| (JsonParse.CRBrack)::uu___ -> begin
     SErr (MissingValue, s)
     end
| uu___ -> begin
     (

let uu___1 = (bare_token s)
in (match (uu___1) with
| (tok, r) -> begin
      
if (is_literal tok) then begin
     SOk ((), r)
     end else begin
     SErr (InvalidLiteral (tok), s)
     end
     end))
     end))
and obj_members : Prims.list<seg>  ->  Prims.list<JsonParse.ch>  ->  Prims.bool  ->  Prims.list<Prims.list<JsonParse.och>>  ->  Prims.list<JsonParse.ch>  ->  sres<unit> = (fun ( pth  :  Prims.list<seg> ) ( start  :  Prims.list<JsonParse.ch> ) ( first  :  Prims.bool ) ( seen  :  Prims.list<Prims.list<JsonParse.och>> ) ( s  :  Prims.list<JsonParse.ch> ) -> (

let w = (JsonParse.skip_ws s)
in (match (w) with
| [] -> begin
     SErr (UnterminatedContainer, start)
     end
| (JsonParse.CRBrace)::t -> begin
      
if first then begin
     SOk ((), t)
     end else begin
     SErr (ExpectedKey, w)
     end
     end
| (JsonParse.CQuote)::t -> begin
     (match ((string_tail pth w [] t)) with
| SErr (why, at) -> begin
     SErr (why, at)
     end
| SOk (key, r1) -> begin
     (

let w1 = (JsonParse.skip_ws r1)
in (match (w1) with
| [] -> begin
     SErr (UnterminatedContainer, start)
     end
| (JsonParse.CColon)::t1 -> begin
     (

let w2 = (JsonParse.skip_ws t1)
in (match (w2) with
| [] -> begin
     SErr (UnterminatedContainer, start)
     end
| uu___ -> begin
     (

let kp = (JsonParse.app pth ((SKey (key))::[]))
in (match ((skip_val kp start w2)) with
| SErr (why, at) -> begin
     SErr (why, at)
     end
| SOk (uu___1, r2) -> begin
      
if (mem_key key seen) then begin
     SErr (RepeatedKey (kp), w)
     end else begin
     (

let w3 = (JsonParse.skip_ws r2)
in (match (w3) with
| [] -> begin
     SErr (UnterminatedContainer, start)
     end
| (JsonParse.CComma)::t3 -> begin
     (obj_members pth start false ((key)::seen) t3)
     end
| (JsonParse.CRBrace)::t3 -> begin
     SOk ((), t3)
     end
| uu___2 -> begin
     SErr (ExpectedCommaOrBrace, w3)
     end))
     end
     end))
     end))
     end
| uu___ -> begin
     SErr (ExpectedColon, w1)
     end))
     end)
     end
| uu___ -> begin
     SErr (ExpectedKey, w)
     end)))
and arr_items : Prims.list<seg>  ->  Prims.list<JsonParse.ch>  ->  Prims.nat  ->  Prims.bool  ->  Prims.list<JsonParse.ch>  ->  sres<unit> = (fun ( pth  :  Prims.list<seg> ) ( start  :  Prims.list<JsonParse.ch> ) ( i  :  Prims.nat ) ( first  :  Prims.bool ) ( s  :  Prims.list<JsonParse.ch> ) -> (

let w = (JsonParse.skip_ws s)
in (match (w) with
| [] -> begin
     SErr (UnterminatedContainer, start)
     end
| (JsonParse.CRBrack)::t -> begin
      
if first then begin
     SOk ((), t)
     end else begin
     SErr (MissingValue, w)
     end
     end
| uu___ -> begin
     (match ((skip_val (JsonParse.app pth ((SIdx (i))::[])) start w)) with
| SErr (why, at) -> begin
     SErr (why, at)
     end
| SOk (uu___1, r) -> begin
     (

let w3 = (JsonParse.skip_ws r)
in (match (w3) with
| [] -> begin
     SErr (UnterminatedContainer, start)
     end
| (JsonParse.CComma)::t3 -> begin
     (arr_items pth start (i + (Prims.parse_int "1")) false t3)
     end
| (JsonParse.CRBrack)::t3 -> begin
     SOk ((), t3)
     end
| uu___2 -> begin
     SErr (ExpectedCommaOrBracket, w3)
     end))
     end)
     end)))


let skip_value : Prims.list<seg>  ->  Prims.list<JsonParse.ch>  ->  sres<unit> = (fun ( pth  :  Prims.list<seg> ) ( s  :  Prims.list<JsonParse.ch> ) -> (skip_val pth s s))


let rec first_break : Prims.nat  ->  Prims.list<JsonParse.ch>  ->  FStar_Pervasives_Native.option<Prims.nat> = (fun ( k  :  Prims.nat ) ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if ((Prims.op_Equals c JsonParse.CNewline) || (Prims.op_Equals c JsonParse.CReturn)) then begin
     FStar_Pervasives_Native.Some (k)
     end else begin
     (first_break (k + (Prims.parse_int "1")) t)
     end
     end))


let check_raw : Prims.list<JsonParse.ch>  ->  res<unit, wreason> = (fun ( raw  :  Prims.list<JsonParse.ch> ) -> (match ((first_break (Prims.parse_int "0") raw)) with
| FStar_Pervasives_Native.Some (k) -> begin
     Error (MultiLine (k))
     end
| FStar_Pervasives_Native.None -> begin
     (match (raw) with
| (c)::uu___ -> begin
      
if (JsonParse.is_ws c) then begin
     Error (NotTrimStable)
     end else begin
     (match ((skip_value [] raw)) with
| SErr (why, uu___1) -> begin
     Error (Unreadable (why))
     end
| SOk (uu___1, []) -> begin
     Ok (())
     end
| SOk (uu___1, rest) -> begin
      
if (all JsonParse.is_ws rest) then begin
     Error (NotTrimStable)
     end else begin
     Error (Unreadable (TrailingContent))
     end
     end)
     end
     end
| [] -> begin
     (match ((skip_value [] raw)) with
| SErr (why, uu___) -> begin
     Error (Unreadable (why))
     end
| SOk (uu___, uu___1) -> begin
     Ok (())
     end)
     end)
     end))

type member1 = {mkey : Prims.list<JsonParse.och>; mspan : Prims.list<JsonParse.ch>; mpos : Prims.nat}


let __proj__Mkmember__item__mkey : member1  ->  Prims.list<JsonParse.och> = (fun ( projectee  :  member1 ) -> (match (projectee) with
| {mkey = mkey; mspan = mspan; mpos = mpos} -> begin
     mkey
     end))


let __proj__Mkmember__item__mspan : member1  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  member1 ) -> (match (projectee) with
| {mkey = mkey; mspan = mspan; mpos = mpos} -> begin
     mspan
     end))


let __proj__Mkmember__item__mpos : member1  ->  Prims.nat = (fun ( projectee  :  member1 ) -> (match (projectee) with
| {mkey = mkey; mspan = mspan; mpos = mpos} -> begin
     mpos
     end))

type jline = {lnum : Prims.nat; ltext : Prims.list<JsonParse.ch>; lfields : Prims.list<member1>}


let __proj__Mkjline__item__lnum : jline  ->  Prims.nat = (fun ( projectee  :  jline ) -> (match (projectee) with
| {lnum = lnum; ltext = ltext; lfields = lfields} -> begin
     lnum
     end))


let __proj__Mkjline__item__ltext : jline  ->  Prims.list<JsonParse.ch> = (fun ( projectee  :  jline ) -> (match (projectee) with
| {lnum = lnum; ltext = ltext; lfields = lfields} -> begin
     ltext
     end))


let __proj__Mkjline__item__lfields : jline  ->  Prims.list<member1> = (fun ( projectee  :  jline ) -> (match (projectee) with
| {lnum = lnum; ltext = ltext; lfields = lfields} -> begin
     lfields
     end))


let rec key_in : Prims.list<JsonParse.och>  ->  Prims.list<member1>  ->  Prims.bool = (fun ( k  :  Prims.list<JsonParse.och> ) ( ms  :  Prims.list<member1> ) -> (match (ms) with
| [] -> begin
     false
     end
| (m)::t -> begin
     ((Prims.op_Equals m.mkey k) || (key_in k t))
     end))


let rec members_loop : Prims.nat  ->  Prims.list<JsonParse.ch>  ->  Prims.list<member1>  ->  Prims.list<JsonParse.ch>  ->  sres<Prims.list<member1>> = (fun ( off  :  Prims.nat ) ( whole  :  Prims.list<JsonParse.ch> ) ( acc  :  Prims.list<member1> ) ( s  :  Prims.list<JsonParse.ch> ) -> (

let w = (JsonParse.skip_ws s)
in (match (w) with
| [] -> begin
     SErr (Truncated, [])
     end
| (JsonParse.CQuote)::t -> begin
     (match ((string_tail [] w [] t)) with
| SErr (why, at) -> begin
     SErr (why, at)
     end
| SOk (key, r1) -> begin
     (

let w1 = (JsonParse.skip_ws r1)
in (match (w1) with
| [] -> begin
     SErr (Truncated, [])
     end
| (JsonParse.CColon)::t1 -> begin
     (

let w2 = (JsonParse.skip_ws t1)
in (match (w2) with
| [] -> begin
     SErr (Truncated, [])
     end
| uu___ -> begin
     (match ((skip_value ((SKey (key))::[]) w2)) with
| SErr (why, at) -> begin
     SErr (why, at)
     end
| SOk (uu___1, r2) -> begin
      
if (key_in key acc) then begin
     SErr (RepeatedKey ((SKey (key))::[]), w)
     end else begin
     (

let m = {mkey = key; mspan = (take_n (sub (len w2) (len r2)) w2); mpos = (off + (pos_in whole w2))}
in (

let w3 = (JsonParse.skip_ws r2)
in (match (w3) with
| [] -> begin
     SErr (Truncated, [])
     end
| (JsonParse.CComma)::t3 -> begin
     (members_loop off whole ((m)::acc) t3)
     end
| (JsonParse.CRBrace)::t3 -> begin
     SOk ((JsonParse.rev ((m)::acc)), t3)
     end
| uu___2 -> begin
     SErr (ExpectedCommaOrBrace, w3)
     end)))
     end
     end)
     end))
     end
| uu___ -> begin
     SErr (ExpectedColon, w1)
     end))
     end)
     end
| uu___ -> begin
     SErr (ExpectedKey, w)
     end)))


let members_end : Prims.nat  ->  Prims.list<JsonParse.ch>  ->  Prims.list<member1>  ->  Prims.list<JsonParse.ch>  ->  res<Prims.list<member1>, (Prims.nat * reason)> = (fun ( off  :  Prims.nat ) ( whole  :  Prims.list<JsonParse.ch> ) ( ms  :  Prims.list<member1> ) ( r  :  Prims.list<JsonParse.ch> ) -> (match ((JsonParse.skip_ws r)) with
| [] -> begin
     Ok (ms)
     end
| w -> begin
     Error ((((off + (pos_in whole w))), (TrailingContent)))
     end))


let members_of : Prims.nat  ->  Prims.list<JsonParse.ch>  ->  res<Prims.list<member1>, (Prims.nat * reason)> = (fun ( off  :  Prims.nat ) ( s  :  Prims.list<JsonParse.ch> ) -> (

let w = (JsonParse.skip_ws s)
in (match (w) with
| (JsonParse.CLBrace)::t -> begin
     (

let w1 = (JsonParse.skip_ws t)
in (match (w1) with
| [] -> begin
     Error ((((off + (pos_in s []))), (Truncated)))
     end
| (JsonParse.CRBrace)::t1 -> begin
     (members_end off s [] t1)
     end
| uu___ -> begin
     (match ((members_loop off s [] w1)) with
| SErr (why, at) -> begin
     Error ((((off + (pos_in s at))), (why)))
     end
| SOk (ms, r) -> begin
     (members_end off s ms r)
     end)
     end))
     end
| uu___ -> begin
     Error ((((off + (pos_in s w))), (NotAnObject)))
     end)))


let parse_line : Prims.nat  ->  Prims.list<JsonParse.ch>  ->  res<jline, fault> = (fun ( number  :  Prims.nat ) ( text  :  Prims.list<JsonParse.ch> ) -> (match ((members_of (Prims.parse_int "0") text)) with
| Ok (ms) -> begin
     Ok ({lnum = number; ltext = text; lfields = ms})
     end
| Error (p, why) -> begin
     Error ((fault_at number p why))
     end))


let top_fields : Prims.list<JsonParse.ch>  ->  res<Prims.list<(Prims.list<JsonParse.och> * Prims.list<JsonParse.ch>)>, fault> = (fun ( line  :  Prims.list<JsonParse.ch> ) -> (match ((parse_line (Prims.parse_int "1") line)) with
| Ok (l) -> begin
     Ok ((map (fun ( m  :  member1 ) -> ((m.mkey), (m.mspan))) l.lfields))
     end
| Error (f) -> begin
     Error (f)
     end))


let rec member_of : Prims.list<JsonParse.ch>  ->  Prims.list<member1>  ->  FStar_Pervasives_Native.option<(Prims.list<JsonParse.ch> * Prims.nat)> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( ms  :  Prims.list<member1> ) -> (match (ms) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (m)::t -> begin
      
if (Prims.op_Equals m.mkey (olit key)) then begin
     FStar_Pervasives_Native.Some (((m.mspan), (m.mpos)))
     end else begin
     (member_of key t)
     end
     end))


let raw_span : Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch>  ->  res<FStar_Pervasives_Native.option<Prims.list<JsonParse.ch>>, fault> = (fun ( field  :  Prims.list<JsonParse.ch> ) ( line  :  Prims.list<JsonParse.ch> ) -> (match ((parse_line (Prims.parse_int "1") line)) with
| Ok (l) -> begin
     (match ((member_of field l.lfields)) with
| FStar_Pervasives_Native.Some (v, uu___) -> begin
     Ok (FStar_Pervasives_Native.Some (v))
     end
| FStar_Pervasives_Native.None -> begin
     Ok (FStar_Pervasives_Native.None)
     end)
     end
| Error (f) -> begin
     Error (f)
     end))


let unquote : Prims.list<JsonParse.ch>  ->  res<Prims.list<JsonParse.och>, reason> = (fun ( raw  :  Prims.list<JsonParse.ch> ) -> (match (raw) with
| (JsonParse.CQuote)::t -> begin
     (match (t) with
| [] -> begin
     Error (ExpectedString ([]))
     end
| uu___ -> begin
     (match ((string_tail [] raw [] t)) with
| SOk (v, []) -> begin
     Ok (v)
     end
| SOk (uu___1, uu___2) -> begin
     Error (ExpectedString ([]))
     end
| SErr (why, uu___1) -> begin
     Error (why)
     end)
     end)
     end
| uu___ -> begin
     Error (ExpectedString ([]))
     end))


let line_number : jline  ->  Prims.nat = (fun ( l  :  jline ) -> l.lnum)


let line_text : jline  ->  Prims.list<JsonParse.ch> = (fun ( l  :  jline ) -> l.ltext)


let refuse : jline  ->  Prims.string  ->  fault = (fun ( l  :  jline ) ( why  :  Prims.string ) -> (fault_at l.lnum (Prims.parse_int "0") (Refused (why))))


let try_raw_field : Prims.list<JsonParse.ch>  ->  jline  ->  FStar_Pervasives_Native.option<Prims.list<JsonParse.ch>> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( l  :  jline ) -> (match ((member_of key l.lfields)) with
| FStar_Pervasives_Native.Some (v, uu___) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end))


let raw_field : Prims.list<JsonParse.ch>  ->  jline  ->  res<Prims.list<JsonParse.ch>, fault> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( l  :  jline ) -> (match ((member_of key l.lfields)) with
| FStar_Pervasives_Native.Some (v, uu___) -> begin
     Ok (v)
     end
| FStar_Pervasives_Native.None -> begin
     Error ((fault_at l.lnum (Prims.parse_int "0") (MissingField (key))))
     end))


let string_field : Prims.list<JsonParse.ch>  ->  jline  ->  res<Prims.list<JsonParse.och>, fault> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( l  :  jline ) -> (match ((member_of key l.lfields)) with
| FStar_Pervasives_Native.None -> begin
     Error ((fault_at l.lnum (Prims.parse_int "0") (MissingField (key))))
     end
| FStar_Pervasives_Native.Some (v, p) -> begin
     (match ((unquote v)) with
| Ok (s) -> begin
     Ok (s)
     end
| Error (ExpectedString (uu___)) -> begin
     Error ((fault_at l.lnum p (ExpectedString (key))))
     end
| Error (why) -> begin
     Error ((fault_at l.lnum p why))
     end)
     end))


let digit_val : JsonParse.ch  ->  Prims.nat = (fun ( c  :  JsonParse.ch ) -> (match (c) with
| JsonParse.CD0 -> begin
     (Prims.parse_int "0")
     end
| JsonParse.CD1 -> begin
     (Prims.parse_int "1")
     end
| JsonParse.CD2 -> begin
     (Prims.parse_int "2")
     end
| JsonParse.CD3 -> begin
     (Prims.parse_int "3")
     end
| JsonParse.CD4 -> begin
     (Prims.parse_int "4")
     end
| JsonParse.CD5 -> begin
     (Prims.parse_int "5")
     end
| JsonParse.CD6 -> begin
     (Prims.parse_int "6")
     end
| JsonParse.CD7 -> begin
     (Prims.parse_int "7")
     end
| JsonParse.CD8 -> begin
     (Prims.parse_int "8")
     end
| JsonParse.CD9 -> begin
     (Prims.parse_int "9")
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))


let rec fold_digits : Prims.nat  ->  Prims.list<JsonParse.ch>  ->  Prims.nat = (fun ( acc  :  Prims.nat ) ( d  :  Prims.list<JsonParse.ch> ) -> (match (d) with
| [] -> begin
     acc
     end
| (c)::t -> begin
     (fold_digits ((acc * (Prims.parse_int "10")) + (digit_val c)) t)
     end))


let int32_max : Prims.nat = (Prims.parse_int "2147483647")


let int32_min_mag : Prims.nat = (Prims.parse_int "2147483648")


let read_int : Prims.list<JsonParse.ch>  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( v  :  Prims.list<JsonParse.ch> ) -> (

let negative = (match (v) with
| (JsonParse.CMinus)::uu___ -> begin
     true
     end
| uu___ -> begin
     false
     end)
in (

let digits = (match (v) with
| (JsonParse.CMinus)::t -> begin
     t
     end
| uu___ -> begin
     v
     end)
in (

let grammatical = ((((match (digits) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) && ((len digits) <= (Prims.parse_int "10"))) && (all JsonParse.is_digit digits)) && ((Prims.op_Equals digits ((JsonParse.CD0)::[])) || (match (digits) with
| (JsonParse.CD0)::uu___ -> begin
     false
     end
| uu___ -> begin
     true
     end)))
in  
if (not (grammatical)) then begin
     FStar_Pervasives_Native.None
     end else begin
     (

let magnitude = (fold_digits (Prims.parse_int "0") digits)
in  
if negative then begin
      
if (magnitude <= int32_min_mag) then begin
     FStar_Pervasives_Native.Some (((Prims.parse_int "0") - magnitude))
     end else begin
     FStar_Pervasives_Native.None
     end
     end else begin
      
if (magnitude <= int32_max) then begin
     FStar_Pervasives_Native.Some (magnitude)
     end else begin
     FStar_Pervasives_Native.None
     end
     end)
     end))))


let int_field : Prims.list<JsonParse.ch>  ->  jline  ->  res<Prims.int, fault> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( l  :  jline ) -> (match ((member_of key l.lfields)) with
| FStar_Pervasives_Native.None -> begin
     Error ((fault_at l.lnum (Prims.parse_int "0") (MissingField (key))))
     end
| FStar_Pervasives_Native.Some (v, p) -> begin
     (match ((read_int v)) with
| FStar_Pervasives_Native.Some (i) -> begin
     Ok (i)
     end
| FStar_Pervasives_Native.None -> begin
     Error ((fault_at l.lnum p (ExpectedInteger (key))))
     end)
     end))


let rec skip_ws1 : Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch> = (fun ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| (c)::t -> begin
     (match (t) with
| [] -> begin
     s
     end
| uu___ -> begin
      
if (JsonParse.is_ws c) then begin
     (skip_ws1 t)
     end else begin
     s
     end
     end)
     end
| [] -> begin
     s
     end))

type sitems =
| Items of Prims.list<Prims.list<JsonParse.och>>
| Bad
| ScanFault of Prims.nat * reason


let uu___is_Items : sitems  ->  Prims.bool = (fun ( projectee  :  sitems ) -> (match (projectee) with
| Items (xs) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Items__item__xs : sitems  ->  Prims.list<Prims.list<JsonParse.och>> = (fun ( projectee  :  sitems ) -> (match (projectee) with
| Items (xs) -> begin
     xs
     end))


let uu___is_Bad : sitems  ->  Prims.bool = (fun ( projectee  :  sitems ) -> (match (projectee) with
| Bad -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ScanFault : sitems  ->  Prims.bool = (fun ( projectee  :  sitems ) -> (match (projectee) with
| ScanFault (at, why) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ScanFault__item__at : sitems  ->  Prims.nat = (fun ( projectee  :  sitems ) -> (match (projectee) with
| ScanFault (at, why) -> begin
     at
     end))


let __proj__ScanFault__item__why : sitems  ->  reason = (fun ( projectee  :  sitems ) -> (match (projectee) with
| ScanFault (at, why) -> begin
     why
     end))


let rec ends_with_rbrack : Prims.list<JsonParse.ch>  ->  Prims.bool = (fun ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| [] -> begin
     false
     end
| (c)::[] -> begin
     (Prims.op_Equals c JsonParse.CRBrack)
     end
| (uu___)::t -> begin
     (ends_with_rbrack t)
     end))


let rec items_loop : Prims.nat  ->  Prims.list<JsonParse.ch>  ->  Prims.bool  ->  Prims.list<Prims.list<JsonParse.och>>  ->  Prims.list<JsonParse.ch>  ->  sitems = (fun ( p  :  Prims.nat ) ( v  :  Prims.list<JsonParse.ch> ) ( expect_item  :  Prims.bool ) ( acc  :  Prims.list<Prims.list<JsonParse.och>> ) ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| (uu___)::(uu___1)::uu___2 -> begin
     (

let w = (skip_ws1 s)
in (match (w) with
| (JsonParse.CQuote)::t -> begin
      
if expect_item then begin
     (match ((string_tail [] w [] t)) with
| SErr (why, at) -> begin
     ScanFault ((p + (pos_in v at)), why)
     end
| SOk (item, r) -> begin
     (items_loop p v false ((item)::acc) (skip_ws1 r))
     end)
     end else begin
     Bad
     end
     end
| (JsonParse.CComma)::t -> begin
      
if expect_item then begin
     Bad
     end else begin
     (items_loop p v true acc (skip_ws1 t))
     end
     end
| uu___3 -> begin
     Bad
     end))
     end
| uu___ -> begin
      
if expect_item then begin
     Bad
     end else begin
     Items ((JsonParse.rev acc))
     end
     end))


let strings_field : Prims.list<JsonParse.ch>  ->  jline  ->  res<Prims.list<Prims.list<JsonParse.och>>, fault> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( l  :  jline ) -> (match ((member_of key l.lfields)) with
| FStar_Pervasives_Native.None -> begin
     Error ((fault_at l.lnum (Prims.parse_int "0") (MissingField (key))))
     end
| FStar_Pervasives_Native.Some (v, p) -> begin
     (

let bad = Error ((fault_at l.lnum p (ExpectedStringArray (key))))
in (match (v) with
| (JsonParse.CLBrack)::t -> begin
      
if (ends_with_rbrack t) then begin
     (match ((skip_ws1 t)) with
| (JsonParse.CRBrack)::[] -> begin
     Ok ([])
     end
| w -> begin
     (match ((items_loop p v true [] w)) with
| Items (xs) -> begin
     Ok (xs)
     end
| Bad -> begin
     bad
     end
| ScanFault (at, why) -> begin
     Error ((fault_at l.lnum at why))
     end)
     end)
     end else begin
     bad
     end
     end
| uu___ -> begin
     bad
     end))
     end))

type actor =
| AHuman of Prims.list<JsonParse.och>
| AAgent of Prims.list<JsonParse.och> * Prims.list<JsonParse.och> * Prims.list<JsonParse.och>


let uu___is_AHuman : actor  ->  Prims.bool = (fun ( projectee  :  actor ) -> (match (projectee) with
| AHuman (id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AHuman__item__id : actor  ->  Prims.list<JsonParse.och> = (fun ( projectee  :  actor ) -> (match (projectee) with
| AHuman (id) -> begin
     id
     end))


let uu___is_AAgent : actor  ->  Prims.bool = (fun ( projectee  :  actor ) -> (match (projectee) with
| AAgent (model, version, id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AAgent__item__model : actor  ->  Prims.list<JsonParse.och> = (fun ( projectee  :  actor ) -> (match (projectee) with
| AAgent (model, version, id) -> begin
     model
     end))


let __proj__AAgent__item__version : actor  ->  Prims.list<JsonParse.och> = (fun ( projectee  :  actor ) -> (match (projectee) with
| AAgent (model, version, id) -> begin
     version
     end))


let __proj__AAgent__item__id : actor  ->  Prims.list<JsonParse.och> = (fun ( projectee  :  actor ) -> (match (projectee) with
| AAgent (model, version, id) -> begin
     id
     end))


let k_seq : Prims.list<JsonParse.ch> = (JsonParse.CLs)::(JsonParse.CLe)::(JsonParse.COther ("q"))::[]


let k_actor : Prims.list<JsonParse.ch> = (JsonParse.CLa)::(JsonParse.CLc)::(JsonParse.CLt)::(JsonParse.COther ("o"))::(JsonParse.CLr)::[]


let k_op : Prims.list<JsonParse.ch> = (JsonParse.COther ("o"))::(JsonParse.COther ("p"))::[]


let k_prev_hash : Prims.list<JsonParse.ch> = (JsonParse.COther ("p"))::(JsonParse.CLr)::(JsonParse.CLe)::(JsonParse.COther ("v"))::(JsonParse.COther ("H"))::(JsonParse.CLa)::(JsonParse.CLs)::(JsonParse.COther ("h"))::[]


let k_hash : Prims.list<JsonParse.ch> = (JsonParse.COther ("h"))::(JsonParse.CLa)::(JsonParse.CLs)::(JsonParse.COther ("h"))::[]


let k_snapshot : Prims.list<JsonParse.ch> = (JsonParse.CLs)::(JsonParse.CLn)::(JsonParse.CLa)::(JsonParse.COther ("p"))::(JsonParse.CLs)::(JsonParse.COther ("h"))::(JsonParse.COther ("o"))::(JsonParse.CLt)::[]


let k_kind : Prims.list<JsonParse.ch> = (JsonParse.COther ("k"))::(JsonParse.COther ("i"))::(JsonParse.CLn)::(JsonParse.CLd)::[]


let k_id : Prims.list<JsonParse.ch> = (JsonParse.COther ("i"))::(JsonParse.CLd)::[]


let k_model : Prims.list<JsonParse.ch> = (JsonParse.COther ("m"))::(JsonParse.COther ("o"))::(JsonParse.CLd)::(JsonParse.CLe)::(JsonParse.CLl)::[]


let k_version : Prims.list<JsonParse.ch> = (JsonParse.COther ("v"))::(JsonParse.CLe)::(JsonParse.CLr)::(JsonParse.CLs)::(JsonParse.COther ("i"))::(JsonParse.COther ("o"))::(JsonParse.CLn)::[]


let lit_human : Prims.list<JsonParse.ch> = (JsonParse.COther ("h"))::(JsonParse.CLu)::(JsonParse.COther ("m"))::(JsonParse.CLa)::(JsonParse.CLn)::[]


let lit_agent : Prims.list<JsonParse.ch> = (JsonParse.CLa)::(JsonParse.COther ("g"))::(JsonParse.CLe)::(JsonParse.CLn)::(JsonParse.CLt)::[]


let dotted : Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( m  :  Prims.list<JsonParse.ch> ) -> (JsonParse.app key ((JsonParse.CDot)::m)))


let actor_id : actor  ->  Prims.list<JsonParse.och> = (fun ( a  :  actor ) -> (match (a) with
| AHuman (id) -> begin
     id
     end
| AAgent (uu___, uu___1, id) -> begin
     id
     end))


let actor_valid : actor  ->  Prims.bool = (fun ( a  :  actor ) -> (match ((actor_id a)) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end))


let actor_str : Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch>  ->  jline  ->  res<Prims.list<JsonParse.och>, fault> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( m  :  Prims.list<JsonParse.ch> ) ( inner  :  jline ) -> (match ((string_field m inner)) with
| Ok (s) -> begin
     Ok (s)
     end
| Error (f) -> begin
     (match (f.freason) with
| MissingField (x) -> begin
     Error ((fault_at f.fline f.fpos (MissingField ((dotted key x)))))
     end
| ExpectedString (x) -> begin
     Error ((fault_at f.fline f.fpos (ExpectedString ((dotted key x)))))
     end
| uu___ -> begin
     Error (f)
     end)
     end))


let named : Prims.list<JsonParse.ch>  ->  jline  ->  Prims.nat  ->  actor  ->  res<actor, fault> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( l  :  jline ) ( p  :  Prims.nat ) ( a  :  actor ) ->  
if (actor_valid a) then begin
     Ok (a)
     end else begin
     Error ((fault_at l.lnum p (ActorInvalid (key))))
     end)


let actor_field : Prims.list<JsonParse.ch>  ->  jline  ->  res<actor, fault> = (fun ( key  :  Prims.list<JsonParse.ch> ) ( l  :  jline ) -> (match ((member_of key l.lfields)) with
| FStar_Pervasives_Native.None -> begin
     Error ((fault_at l.lnum (Prims.parse_int "0") (MissingField (key))))
     end
| FStar_Pervasives_Native.Some (v, p) -> begin
     (match ((members_of p v)) with
| Error (q, why) -> begin
     Error ((fault_at l.lnum q why))
     end
| Ok (ms) -> begin
     (

let inner = {lnum = l.lnum; ltext = v; lfields = ms}
in (match ((try_raw_field k_kind inner)) with
| FStar_Pervasives_Native.None -> begin
     Error ((fault_at l.lnum (Prims.parse_int "0") RefusedNoKind))
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     (match ((actor_str key k_kind inner)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (kind) -> begin
      
if (Prims.op_Equals kind (olit lit_human)) then begin
     (match ((actor_str key k_id inner)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (id) -> begin
     (named key l p (AHuman (id)))
     end)
     end else begin
      
if (Prims.op_Equals kind (olit lit_agent)) then begin
     (match ((actor_str key k_model inner)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (model) -> begin
     (match ((actor_str key k_version inner)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (version) -> begin
     (match ((actor_str key k_id inner)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (id) -> begin
     (named key l p (AAgent (model, version, id)))
     end)
     end)
     end)
     end else begin
     Error ((fault_at l.lnum (Prims.parse_int "0") (RefusedKind (kind))))
     end
     end
     end)
     end))
     end)
     end))


let rec split_lines : Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch>  ->  Prims.list<Prims.list<JsonParse.ch>> = (fun ( cur  :  Prims.list<JsonParse.ch> ) ( s  :  Prims.list<JsonParse.ch> ) -> (match (s) with
| [] -> begin
     ((JsonParse.rev cur))::[]
     end
| (JsonParse.CReturn)::(JsonParse.CNewline)::t -> begin
     ((JsonParse.rev cur))::(split_lines [] t)
     end
| (JsonParse.CNewline)::t -> begin
     ((JsonParse.rev cur))::(split_lines [] t)
     end
| (c)::t -> begin
     (split_lines ((c)::cur) t)
     end))


let split : Prims.list<JsonParse.ch>  ->  Prims.list<Prims.list<JsonParse.ch>> = (fun ( text  :  Prims.list<JsonParse.ch> ) -> (split_lines [] text))


let is_blank : (JsonParse.ch  ->  Prims.bool)  ->  JsonParse.ch  ->  Prims.bool = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( c  :  JsonParse.ch ) -> (match (c) with
| JsonParse.COther (uu___) -> begin
     ((JsonParse.is_ws c) || (ws c))
     end
| uu___ -> begin
     (JsonParse.is_ws c)
     end))


let blank_line : (JsonParse.ch  ->  Prims.bool)  ->  Prims.list<JsonParse.ch>  ->  Prims.bool = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( l  :  Prims.list<JsonParse.ch> ) -> (all (is_blank ws) l))


let rec scan_from = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( dec  :  jline  ->  res<'a, fault> ) ( k  :  Prims.nat ) ( lines  :  Prims.list<Prims.list<JsonParse.ch>> ) ( acc  :  Prims.list<'a> ) -> (match (lines) with
| [] -> begin
     Ok ((JsonParse.rev acc))
     end
| (l)::rest -> begin
      
if (blank_line ws l) then begin
     (scan_from ws dec (k + (Prims.parse_int "1")) rest acc)
     end else begin
     (match ((parse_line (k + (Prims.parse_int "1")) l)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (line) -> begin
     (match ((dec line)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (v) -> begin
     (scan_from ws dec (k + (Prims.parse_int "1")) rest ((v)::acc))
     end)
     end)
     end
     end))


let scan_records = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( dec  :  jline  ->  res<'a, fault> ) ( text  :  Prims.list<JsonParse.ch> ) -> (scan_from ws dec (Prims.parse_int "0") (split text) []))

type lrec<'op> = {lseq : Prims.int; lactor : actor; lop : 'op; lprev : Prims.list<JsonParse.och>; lhash : Prims.list<JsonParse.och>}


let __proj__Mklrec__item__lseq = (fun ( projectee  :  lrec<'op> ) -> (match (projectee) with
| {lseq = lseq; lactor = lactor; lop = lop; lprev = lprev; lhash = lhash} -> begin
     lseq
     end))


let __proj__Mklrec__item__lactor = (fun ( projectee  :  lrec<'op> ) -> (match (projectee) with
| {lseq = lseq; lactor = lactor; lop = lop; lprev = lprev; lhash = lhash} -> begin
     lactor
     end))


let __proj__Mklrec__item__lop = (fun ( projectee  :  lrec<'op> ) -> (match (projectee) with
| {lseq = lseq; lactor = lactor; lop = lop; lprev = lprev; lhash = lhash} -> begin
     lop
     end))


let __proj__Mklrec__item__lprev = (fun ( projectee  :  lrec<'op> ) -> (match (projectee) with
| {lseq = lseq; lactor = lactor; lop = lop; lprev = lprev; lhash = lhash} -> begin
     lprev
     end))


let __proj__Mklrec__item__lhash = (fun ( projectee  :  lrec<'op> ) -> (match (projectee) with
| {lseq = lseq; lactor = lactor; lop = lop; lprev = lprev; lhash = lhash} -> begin
     lhash
     end))

type witness<'op> = {enc : 'op  ->  Prims.list<JsonParse.ch>; dec : Prims.list<JsonParse.ch>  ->  res<'op, Prims.string>}


let __proj__Mkwitness__item__enc = (fun ( projectee  :  witness<'op> ) -> (match (projectee) with
| {enc = enc; dec = dec} -> begin
     enc
     end))


let __proj__Mkwitness__item__dec = (fun ( projectee  :  witness<'op> ) -> (match (projectee) with
| {enc = enc; dec = dec} -> begin
     dec
     end))


let rec quote_body : Prims.list<JsonParse.och>  ->  Prims.list<JsonParse.ch> = (fun ( s  :  Prims.list<JsonParse.och> ) -> (match (s) with
| [] -> begin
     []
     end
| (JsonParse.OLit (JsonParse.CQuote))::t -> begin
     (JsonParse.CBackslash)::(JsonParse.CQuote)::(quote_body t)
     end
| (JsonParse.OLit (JsonParse.CBackslash))::t -> begin
     (JsonParse.CBackslash)::(JsonParse.CBackslash)::(quote_body t)
     end
| (JsonParse.OLit (JsonParse.CTab))::t -> begin
     (JsonParse.CBackslash)::(JsonParse.CLu)::(JsonParse.CD0)::(JsonParse.CD0)::(JsonParse.CD0)::(JsonParse.CD9)::(quote_body t)
     end
| (JsonParse.OLit (JsonParse.CNewline))::t -> begin
     (JsonParse.CBackslash)::(JsonParse.CLu)::(JsonParse.CD0)::(JsonParse.CD0)::(JsonParse.CD0)::(JsonParse.CLa)::(quote_body t)
     end
| (JsonParse.OLit (JsonParse.CReturn))::t -> begin
     (JsonParse.CBackslash)::(JsonParse.CLu)::(JsonParse.CD0)::(JsonParse.CD0)::(JsonParse.CD0)::(JsonParse.CLd)::(quote_body t)
     end
| (JsonParse.OLit (c))::t -> begin
     (c)::(quote_body t)
     end
| (JsonParse.OEsc (e))::t -> begin
     (JsonParse.CBackslash)::(e)::(quote_body t)
     end
| (JsonParse.OUni (a, b, c, d))::t -> begin
     (JsonParse.CBackslash)::(JsonParse.CLu)::(a)::(b)::(c)::(d)::(quote_body t)
     end))


let quote_str : Prims.list<JsonParse.och>  ->  Prims.list<JsonParse.ch> = (fun ( s  :  Prims.list<JsonParse.och> ) -> (JsonParse.CQuote)::(JsonParse.app (quote_body s) ((JsonParse.CQuote)::[])))


let member_text : Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.ch> = (fun ( k  :  Prims.list<JsonParse.ch> ) ( v  :  Prims.list<JsonParse.ch> ) -> (JsonParse.CQuote)::(JsonParse.app k ((JsonParse.CQuote)::(JsonParse.CColon)::v)))


let obj_text : Prims.list<Prims.list<JsonParse.ch>>  ->  Prims.list<JsonParse.ch> = (fun ( ms  :  Prims.list<Prims.list<JsonParse.ch>> ) -> (JsonParse.CLBrace)::(JsonParse.app (join_sep JsonParse.CComma ms) ((JsonParse.CRBrace)::[])))


let encode_actor : actor  ->  Prims.list<JsonParse.ch> = (fun ( a  :  actor ) -> (match (a) with
| AHuman (id) -> begin
     (obj_text (((member_text k_kind (quote_str (olit lit_human))))::((member_text k_id (quote_str id)))::[]))
     end
| AAgent (model, version, id) -> begin
     (obj_text (((member_text k_kind (quote_str (olit lit_agent))))::((member_text k_model (quote_str model)))::((member_text k_version (quote_str version)))::((member_text k_id (quote_str id)))::[]))
     end))


let digit_ch : Prims.nat  ->  JsonParse.ch = (fun ( d  :  Prims.nat ) -> (match (d) with
| uu___ when (uu___ = (Prims.parse_int "0")) -> begin
     JsonParse.CD0
     end
| uu___ when (uu___ = (Prims.parse_int "1")) -> begin
     JsonParse.CD1
     end
| uu___ when (uu___ = (Prims.parse_int "2")) -> begin
     JsonParse.CD2
     end
| uu___ when (uu___ = (Prims.parse_int "3")) -> begin
     JsonParse.CD3
     end
| uu___ when (uu___ = (Prims.parse_int "4")) -> begin
     JsonParse.CD4
     end
| uu___ when (uu___ = (Prims.parse_int "5")) -> begin
     JsonParse.CD5
     end
| uu___ when (uu___ = (Prims.parse_int "6")) -> begin
     JsonParse.CD6
     end
| uu___ when (uu___ = (Prims.parse_int "7")) -> begin
     JsonParse.CD7
     end
| uu___ when (uu___ = (Prims.parse_int "8")) -> begin
     JsonParse.CD8
     end
| uu___ -> begin
     JsonParse.CD9
     end))


let rec digits_of_nat : Prims.nat  ->  Prims.list<JsonParse.ch> = (fun ( n  :  Prims.nat ) ->  
if (n < (Prims.parse_int "10")) then begin
     ((digit_ch n))::[]
     end else begin
     (JsonParse.app (digits_of_nat (n / (Prims.parse_int "10"))) (((digit_ch (Prims.mod_f n (Prims.parse_int "10"))))::[]))
     end)


let show_int : Prims.int  ->  Prims.list<JsonParse.ch> = (fun ( s  :  Prims.int ) ->  
if (s < (Prims.parse_int "0")) then begin
     (JsonParse.CMinus)::(digits_of_nat ((Prims.parse_int "0") - s))
     end else begin
     (digits_of_nat s)
     end)


let line_of : Prims.int  ->  actor  ->  Prims.list<JsonParse.ch>  ->  Prims.list<JsonParse.och>  ->  Prims.list<JsonParse.och>  ->  Prims.list<JsonParse.ch> = (fun ( seq  :  Prims.int ) ( a  :  actor ) ( op_json  :  Prims.list<JsonParse.ch> ) ( prev  :  Prims.list<JsonParse.och> ) ( hash  :  Prims.list<JsonParse.och> ) -> (obj_text (((member_text k_seq (show_int seq)))::((member_text k_actor (encode_actor a)))::((member_text k_op op_json))::((member_text k_prev_hash (quote_str prev)))::((member_text k_hash (quote_str hash)))::[])))


let record_line = (fun ( r  :  lrec<'op> ) ( op_json  :  Prims.list<JsonParse.ch> ) -> (line_of r.lseq r.lactor op_json r.lprev r.lhash))


let to_jsonl = (fun ( w  :  witness<'op> ) ( rs  :  Prims.list<lrec<'op>> ) -> (join_sep JsonParse.CNewline (map (fun ( r  :  lrec<'op> ) -> (record_line r (w.enc r.lop))) rs)))


let record_members = (fun ( w  :  witness<'op> ) ( r  :  lrec<'op> ) -> (((k_actor), ((encode_actor r.lactor))))::(((k_op), ((w.enc r.lop))))::(((k_prev_hash), ((quote_str r.lprev))))::(((k_hash), ((quote_str r.lhash))))::[])


let rec first_unreadable : Prims.list<(Prims.list<JsonParse.ch> * Prims.list<JsonParse.ch>)>  ->  FStar_Pervasives_Native.option<(Prims.list<JsonParse.ch> * wreason)> = (fun ( ms  :  Prims.list<(Prims.list<JsonParse.ch> * Prims.list<JsonParse.ch>)> ) -> (match (ms) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((k, t))::rest -> begin
     (match ((check_raw t)) with
| Error (why) -> begin
     FStar_Pervasives_Native.Some (((k), (why)))
     end
| Ok (uu___) -> begin
     (first_unreadable rest)
     end)
     end))


let rec checked_lines = (fun ( w  :  witness<'op> ) ( i  :  Prims.nat ) ( rs  :  Prims.list<lrec<'op>> ) -> (match (rs) with
| [] -> begin
     Ok ([])
     end
| (r)::rest -> begin
     (match ((first_unreadable (record_members w r))) with
| FStar_Pervasives_Native.Some (k, why) -> begin
     Error ({wline = (i + (Prims.parse_int "1")); wmember = k; wreason = why})
     end
| FStar_Pervasives_Native.None -> begin
     (match ((checked_lines w (i + (Prims.parse_int "1")) rest)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (ls) -> begin
     Ok (((record_line r (w.enc r.lop)))::ls)
     end)
     end)
     end))


let try_to_jsonl = (fun ( w  :  witness<'op> ) ( rs  :  Prims.list<lrec<'op>> ) -> (match ((checked_lines w (Prims.parse_int "0") rs)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (ls) -> begin
     Ok ((join_sep JsonParse.CNewline ls))
     end))


let record_of = (fun ( actor_of  :  jline  ->  res<actor, fault> ) ( w  :  witness<'op> ) ( l  :  jline ) -> (match ((int_field k_seq l)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (seq) -> begin
     (match ((actor_of l)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (a) -> begin
     (match ((raw_field k_op l)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (raw) -> begin
     (match ((w.dec raw)) with
| Error (why) -> begin
     Error ((refuse l why))
     end
| Ok (o) -> begin
     (match ((string_field k_prev_hash l)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (prev) -> begin
     (match ((string_field k_hash l)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (hash) -> begin
     Ok ({lseq = seq; lactor = a; lop = o; lprev = prev; lhash = hash})
     end)
     end)
     end)
     end)
     end)
     end))


let is_snapshot_line : jline  ->  Prims.bool = (fun ( l  :  jline ) -> ((match ((try_raw_field k_op l)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end) && (Prims.op_Equals (try_raw_field k_snapshot l) (FStar_Pervasives_Native.Some (lit_true)))))

type item<'op> =
| Rec of Prims.nat * lrec<'op>
| Snap of jline


let uu___is_Rec = (fun ( projectee  :  item<'op> ) -> (match (projectee) with
| Rec (line, r) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Rec__item__line = (fun ( projectee  :  item<'op> ) -> (match (projectee) with
| Rec (line, r) -> begin
     line
     end))


let __proj__Rec__item__r = (fun ( projectee  :  item<'op> ) -> (match (projectee) with
| Rec (line, r) -> begin
     r
     end))


let uu___is_Snap = (fun ( projectee  :  item<'op> ) -> (match (projectee) with
| Snap (l) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Snap__item__l = (fun ( projectee  :  item<'op> ) -> (match (projectee) with
| Snap (l) -> begin
     l
     end))


let line_item = (fun ( actor_of  :  jline  ->  res<actor, fault> ) ( w  :  witness<'op> ) ( l  :  jline ) ->  
if (is_snapshot_line l) then begin
     Ok (Snap (l))
     end else begin
     (match ((record_of actor_of w l)) with
| Ok (r) -> begin
     Ok (Rec (l.lnum, r))
     end
| Error (f) -> begin
     Error (f)
     end)
     end)


let rec settle_items = (fun ( first  :  Prims.bool ) ( recs  :  Prims.list<(Prims.nat * lrec<'op>)> ) ( snaps  :  Prims.list<Prims.list<JsonParse.ch>> ) ( items  :  Prims.list<item<'op>> ) -> (match (items) with
| [] -> begin
     Ok ((((JsonParse.rev recs)), ((JsonParse.rev snaps))))
     end
| (Rec (n, r))::rest -> begin
     (settle_items false ((((n), (r)))::recs) snaps rest)
     end
| (Snap (l))::rest -> begin
      
if first then begin
     (settle_items false recs ((l.ltext)::[]) rest)
     end else begin
     Error ((fault_at l.lnum (Prims.parse_int "0") SnapshotNotAtHead))
     end
     end))


let scan_with_snapshots = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( actor_of  :  jline  ->  res<actor, fault> ) ( w  :  witness<'op> ) ( text  :  Prims.list<JsonParse.ch> ) -> (match ((scan_records ws (line_item actor_of w) text)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (items) -> begin
     (settle_items true [] [] items)
     end))


type carried<'op> = (Prims.list<JsonParse.ch> * res<'op, Prims.string>)


let carry = (fun ( w  :  witness<'op> ) -> {enc = (fun ( c  :  carried<'op> ) -> (match ((FStar_Pervasives_Native.snd c)) with
| Ok (o) -> begin
     (w.enc o)
     end
| Error (uu___) -> begin
     (FStar_Pervasives_Native.fst c)
     end)); dec = (fun ( raw  :  Prims.list<JsonParse.ch> ) -> Ok (((raw), ((w.dec raw)))))})


let uncarry = (fun ( r  :  lrec<carried<'op>> ) ( o  :  'op ) -> {lseq = r.lseq; lactor = r.lactor; lop = o; lprev = r.lprev; lhash = r.lhash})


let rec decoded_of = (fun ( reads  :  Prims.list<(Prims.nat * lrec<carried<'op>>)> ) -> (match (reads) with
| [] -> begin
     []
     end
| ((line, r))::rest -> begin
     ((match ((FStar_Pervasives_Native.snd r.lop)) with
| Ok (o) -> begin
     Ok ((uncarry r o))
     end
| Error (why) -> begin
     Error ({uline = line; unode = r.lhash; ureason = why})
     end))::(decoded_of rest)
     end))


let rec sites_of = (fun ( d  :  Prims.list<res<lrec<'op>, usite>> ) -> (match (d) with
| [] -> begin
     []
     end
| (Error (s))::t -> begin
     (s)::(sites_of t)
     end
| (Ok (uu___))::t -> begin
     (sites_of t)
     end))


let rec oks_of = (fun ( d  :  Prims.list<res<lrec<'op>, usite>> ) -> (match (d) with
| [] -> begin
     []
     end
| (Ok (r))::t -> begin
     (r)::(oks_of t)
     end
| (Error (uu___))::t -> begin
     (oks_of t)
     end))


let settle = (fun ( reads  :  Prims.list<(Prims.nat * lrec<carried<'op>>)> ) -> (

let d = (decoded_of reads)
in (match ((sites_of d)) with
| [] -> begin
     Ok ((oks_of d))
     end
| sites -> begin
     Error (LUndecodable (sites))
     end)))


let read_carried = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( actor_of  :  jline  ->  res<actor, fault> ) ( w  :  witness<'op> ) ( text  :  Prims.list<JsonParse.ch> ) -> (match ((scan_with_snapshots ws actor_of (carry w) text)) with
| Error (f) -> begin
     Error (LUnreadable (f))
     end
| Ok (v) -> begin
     Ok (v)
     end))


let rec snd_all = (fun ( l  :  Prims.list<('a * 'b)> ) -> (match (l) with
| [] -> begin
     []
     end
| ((uu___, y))::t -> begin
     (y)::(snd_all t)
     end))


let from_jsonl_with_snapshots = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( w  :  witness<'op> ) ( text  :  Prims.list<JsonParse.ch> ) -> (match ((read_carried ws (actor_field k_actor) w text)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (reads, snaps) -> begin
     (match ((settle reads)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (recs) -> begin
     Ok (((recs), (snaps)))
     end)
     end))


let from_jsonl = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( w  :  witness<'op> ) ( text  :  Prims.list<JsonParse.ch> ) -> (match ((from_jsonl_with_snapshots ws w text)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (recs, uu___) -> begin
     Ok (recs)
     end))


let legacy_actor : jline  ->  res<actor, fault> = (fun ( l  :  jline ) -> (match ((string_field k_actor l)) with
| Ok (s) -> begin
     Ok (AHuman (s))
     end
| Error (f) -> begin
     Error (f)
     end))


let from_jsonl_legacy_actor = (fun ( ws  :  JsonParse.ch  ->  Prims.bool ) ( w  :  witness<'op> ) ( text  :  Prims.list<JsonParse.ch> ) -> (match ((read_carried ws legacy_actor w text)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (reads, uu___) -> begin
     (settle reads)
     end))


let rec pos_of_nat : Prims.nat  ->  Chain.pos = (fun ( n  :  Prims.nat ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     Chain.PZero
     end else begin
     Chain.PSucc ((pos_of_nat (n - (Prims.parse_int "1"))))
     end)


let rec nat_of_pos : Chain.pos  ->  Prims.nat = (fun ( p  :  Chain.pos ) -> (match (p) with
| Chain.PZero -> begin
     (Prims.parse_int "0")
     end
| Chain.PSucc (q) -> begin
     ((Prims.parse_int "1") + (nat_of_pos q))
     end))


let pos_of_seq : Prims.nat  ->  Prims.int  ->  Chain.pos = (fun ( n  :  Prims.nat ) ( s  :  Prims.int ) ->  
if (((Prims.parse_int "0") <= s) && (s < n)) then begin
     (pos_of_nat s)
     end else begin
     (pos_of_nat n)
     end)


let show_pos : Chain.pos  ->  Prims.string = (fun ( p  :  Chain.pos ) -> (JsonParse.chs_str (digits_of_nat (nat_of_pos p))))


let enc_carried = (fun ( w  :  witness<'op> ) ( c  :  carried<'op> ) -> (JsonParse.chs_str (match ((FStar_Pervasives_Native.snd c)) with
| Ok (o) -> begin
     (w.enc o)
     end
| Error (uu___) -> begin
     (FStar_Pervasives_Native.fst c)
     end)))


let actor_string : (Prims.string  ->  Prims.string)  ->  (Prims.list<JsonParse.och>  ->  Prims.string)  ->  actor  ->  Prims.string = (fun ( qh  :  Prims.string  ->  Prims.string ) ( ren  :  Prims.list<JsonParse.och>  ->  Prims.string ) ( a  :  actor ) -> (match (a) with
| AHuman (id) -> begin
     (Prims.strcat "{\"kind\":\"human\",\"id\":" (Prims.strcat (qh (ren id)) "}"))
     end
| AAgent (model, version, id) -> begin
     (Prims.strcat "{\"kind\":\"agent\",\"model\":" (Prims.strcat (qh (ren model)) (Prims.strcat ",\"version\":" (Prims.strcat (qh (ren version)) (Prims.strcat ",\"id\":" (Prims.strcat (qh (ren id)) "}"))))))
     end))


let chain_record = (fun ( qh  :  Prims.string  ->  Prims.string ) ( ren  :  Prims.list<JsonParse.och>  ->  Prims.string ) ( n  :  Prims.nat ) ( r  :  lrec<'op> ) -> {Chain.rseq = (pos_of_seq n r.lseq); Chain.ractor = (actor_string qh ren r.lactor); Chain.rop = r.lop; Chain.rprev = (ren r.lprev); Chain.rhash = (ren r.lhash)})


let rec chain_records_from = (fun ( qh  :  Prims.string  ->  Prims.string ) ( ren  :  Prims.list<JsonParse.och>  ->  Prims.string ) ( n  :  Prims.nat ) ( rs  :  Prims.list<lrec<'op>> ) -> (match (rs) with
| [] -> begin
     []
     end
| (r)::t -> begin
     ((chain_record qh ren n r))::(chain_records_from qh ren n t)
     end))


let chain_records = (fun ( qh  :  Prims.string  ->  Prims.string ) ( ren  :  Prims.list<JsonParse.och>  ->  Prims.string ) ( rs  :  Prims.list<lrec<'op>> ) -> (chain_records_from qh ren (len rs) rs))


let genesis : Prims.string = ""


let from_jsonl_verified = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( qh  :  Prims.string  ->  Prims.string ) ( ren  :  Prims.list<JsonParse.och>  ->  Prims.string ) ( ws  :  JsonParse.ch  ->  Prims.bool ) ( w  :  witness<'op> ) ( text  :  Prims.list<JsonParse.ch> ) -> (match ((read_carried ws (actor_field k_actor) w text)) with
| Error (f) -> begin
     Error (f)
     end
| Ok (reads, uu___) -> begin
     (match ((Chain.first_chain_break h show_pos (enc_carried w) genesis (chain_records qh ren (snd_all reads)))) with
| Chain.Found (b) -> begin
     Error (LBroken (b))
     end
| Chain.Missing -> begin
     (settle reads)
     end)
     end))


let rec line_at : Prims.list<Prims.list<JsonParse.ch>>  ->  Prims.nat  ->  Prims.list<JsonParse.ch> = (fun ( ls  :  Prims.list<Prims.list<JsonParse.ch>> ) ( i  :  Prims.nat ) -> (match (ls) with
| [] -> begin
     []
     end
| (l)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     l
     end else begin
     (line_at t (i - (Prims.parse_int "1")))
     end
     end))

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


let twin_line1 : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CD1)::(JsonParse.CRBrace)::[]


let twin_cut : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::[]


let twin_arr : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CLBrack)::(JsonParse.CQuote)::(JsonParse.COther ("x"))::(JsonParse.CQuote)::(JsonParse.CComma)::(JsonParse.CSpace)::(JsonParse.CQuote)::(JsonParse.COther ("y"))::(JsonParse.CQuote)::(JsonParse.CRBrack)::(JsonParse.CRBrace)::[]


let twin_actor_line : Prims.list<JsonParse.ch> = (obj_text (((member_text k_actor (encode_actor (AHuman ((JsonParse.OLit (JsonParse.CLu))::[])))))::[]))


let twin_w : witness<Prims.list<JsonParse.ch>> = {enc = (fun ( o  :  Prims.list<JsonParse.ch> ) -> o); dec = (fun ( raw  :  Prims.list<JsonParse.ch> ) -> Ok (raw))}


let twin_ws : JsonParse.ch  ->  Prims.bool = (fun ( uu___  :  JsonParse.ch ) -> false)


let twin_rec : lrec<Prims.list<JsonParse.ch>> = {lseq = (Prims.parse_int "0"); lactor = AHuman ((JsonParse.OLit (JsonParse.CLu))::[]); lop = (JsonParse.CLBrace)::(JsonParse.CRBrace)::[]; lprev = []; lhash = (JsonParse.OLit (JsonParse.CLa))::(JsonParse.OEsc (JsonParse.CLn))::[]}


let twin_strings : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((parse_line (Prims.parse_int "1") twin_arr)) with
| Ok (l) -> begin
     (Prims.op_Equals (strings_field ((JsonParse.CLa)::[]) l) (Ok (((JsonParse.OLit (JsonParse.COther ("x")))::[])::((JsonParse.OLit (JsonParse.COther ("y")))::[])::[])))
     end
| Error (uu___1) -> begin
     false
     end))


let twin_human : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((parse_line (Prims.parse_int "1") twin_actor_line)) with
| Ok (l) -> begin
     (Prims.op_Equals (actor_field k_actor l) (Ok (AHuman ((JsonParse.OLit (JsonParse.CLu))::[]))))
     end
| Error (uu___1) -> begin
     false
     end))


let twin_round_trip : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((try_to_jsonl twin_w ((twin_rec)::[]))) with
| Ok (text) -> begin
     (Prims.op_Equals (from_jsonl twin_ws twin_w text) (Ok ((twin_rec)::[])))
     end
| Error (uu___1) -> begin
     false
     end))


let twin_second_line : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((scan_records twin_ws (fun ( l  :  jline ) -> Ok (l.lnum)) (JsonParse.app twin_line1 ((JsonParse.CNewline)::twin_cut)))) with
| Error (f) -> begin
     ((Prims.op_Equals f.fline (Prims.parse_int "2")) && (Prims.op_Equals f.freason Truncated))
     end
| Ok (uu___1) -> begin
     false
     end))


let twin_dup : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CD1)::(JsonParse.CComma)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CD2)::(JsonParse.CRBrace)::[]


let twin_nested_dup : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLb)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CD1)::(JsonParse.CComma)::(JsonParse.CQuote)::(JsonParse.CLb)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CD2)::(JsonParse.CRBrace)::(JsonParse.CRBrace)::[]


let twin_tab : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CQuote)::(JsonParse.CTab)::(JsonParse.CQuote)::(JsonParse.CRBrace)::[]


let twin_lone : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CLBrack)::(JsonParse.CQuote)::(JsonParse.CHiSur ("h"))::(JsonParse.CQuote)::(JsonParse.CRBrack)::(JsonParse.CRBrace)::[]


let twin_mixed : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CQuote)::(JsonParse.CBackslash)::(JsonParse.CLu)::(JsonParse.CUd)::(JsonParse.CD8)::(JsonParse.CD0)::(JsonParse.CD0)::(JsonParse.CLoSur ("l"))::(JsonParse.CQuote)::(JsonParse.CRBrace)::[]


let twin_arr_sep : Prims.list<JsonParse.ch> = (JsonParse.CLBrace)::(JsonParse.CQuote)::(JsonParse.CLa)::(JsonParse.CQuote)::(JsonParse.CColon)::(JsonParse.CLBrack)::(JsonParse.CD1)::(JsonParse.CSpace)::(JsonParse.CD2)::(JsonParse.CRBrack)::(JsonParse.CRBrace)::[]


let twin_mixed_reads : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((parse_line (Prims.parse_int "1") twin_mixed)) with
| Ok (v) -> begin
     true
     end
| uu___1 -> begin
     false
     end))


let twin_lone_rec : lrec<Prims.list<JsonParse.ch>> = {lseq = (Prims.parse_int "0"); lactor = AHuman ((JsonParse.OLit (JsonParse.CHiSur ("h")))::[]); lop = (JsonParse.CLBrace)::(JsonParse.CRBrace)::[]; lprev = []; lhash = (JsonParse.OLit (JsonParse.CLa))::[]}


let twin_writer_refuses : unit  ->  Prims.bool = (fun ( uu___  :  unit ) -> (match ((try_to_jsonl twin_w ((twin_lone_rec)::[]))) with
| Error (f) -> begin
     (((Prims.op_Equals f.wline (Prims.parse_int "1")) && (Prims.op_Equals f.wmember k_actor)) && (match (f.wreason) with
| Unreadable (r) -> begin
     true
     end
| uu___1 -> begin
     false
     end))
     end
| Ok (uu___1) -> begin
     false
     end))


let twins : Prims.list<twin> = ({tname = "a-repeated-key-is-refused-at-its-second-quote"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_line (Prims.parse_int "1") twin_dup) (Error ({fline = (Prims.parse_int "1"); fpos = (Prims.parse_int "7"); freason = RepeatedKey ((SKey ((JsonParse.OLit (JsonParse.CLa))::[]))::[])}))))})::({tname = "a-repeated-key-in-a-value-is-refused-at-its-path"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_line (Prims.parse_int "1") twin_nested_dup) (Error ({fline = (Prims.parse_int "1"); fpos = (Prims.parse_int "12"); freason = RepeatedKey ((SKey ((JsonParse.OLit (JsonParse.CLa))::[]))::(SKey ((JsonParse.OLit (JsonParse.CLb))::[]))::[])}))))})::({tname = "a-raw-control-in-a-string-is-refused-at-the-character"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_line (Prims.parse_int "1") twin_tab) (Error ({fline = (Prims.parse_int "1"); fpos = (Prims.parse_int "6"); freason = RawControl ((SKey ((JsonParse.OLit (JsonParse.CLa))::[]))::[])}))))})::({tname = "a-raw-lone-surrogate-is-refused-at-the-unit"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_line (Prims.parse_int "1") twin_lone) (Error ({fline = (Prims.parse_int "1"); fpos = (Prims.parse_int "7"); freason = LoneSurrogate ((SKey ((JsonParse.OLit (JsonParse.CLa))::[]))::(SIdx ((Prims.parse_int "0")))::[])}))))})::({tname = "a-pair-split-between-spellings-is-read"; tholds = twin_mixed_reads})::({tname = "the-writer-refuses-a-string-the-reader-would-refuse"; tholds = twin_writer_refuses})::({tname = "array-items-need-a-separator"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_line (Prims.parse_int "1") twin_arr_sep) (Error ({fline = (Prims.parse_int "1"); fpos = (Prims.parse_int "8"); freason = ExpectedCommaOrBracket}))))})::({tname = "parse-line-reads-one-member-at-its-position"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_line (Prims.parse_int "1") twin_line1) (Ok ({lnum = (Prims.parse_int "1"); ltext = twin_line1; lfields = ({mkey = (JsonParse.OLit (JsonParse.CLa))::[]; mspan = (JsonParse.CD1)::[]; mpos = (Prims.parse_int "5")})::[]}))))})::({tname = "a-truncated-line-is-refused-past-its-end"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_line (Prims.parse_int "3") twin_cut) (Error ({fline = (Prims.parse_int "3"); fpos = (Prims.parse_int "5"); freason = Truncated}))))})::({tname = "unquote-reads-back-what-quote-wrote"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (unquote (quote_str ((JsonParse.OLit (JsonParse.CLa))::(JsonParse.OEsc (JsonParse.CLn))::(JsonParse.OUni (JsonParse.CD0, JsonParse.CD0, JsonParse.CD0, JsonParse.CD1))::[]))) (Ok ((JsonParse.OLit (JsonParse.CLa))::(JsonParse.OEsc (JsonParse.CLn))::(JsonParse.OUni (JsonParse.CD0, JsonParse.CD0, JsonParse.CD0, JsonParse.CD1))::[]))))})::({tname = "read-int-reads-a-negative-numeral"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (read_int ((JsonParse.CMinus)::(JsonParse.CD4)::(JsonParse.CD2)::[])) (FStar_Pervasives_Native.Some ((Prims.parse_int "-42")))))})::({tname = "read-int-refuses-a-leading-zero"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (read_int ((JsonParse.CD0)::(JsonParse.CD7)::[])) FStar_Pervasives_Native.None))})::({tname = "check-raw-admits-an-object-and-refuses-trailing-space"; tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (check_raw ((JsonParse.CLBrace)::(JsonParse.CRBrace)::[])) (Ok (()))) && (Prims.op_Equals (check_raw ((JsonParse.CD1)::(JsonParse.CSpace)::[])) (Error (NotTrimStable)))))})::({tname = "split-lines-normalises-crlf"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (split ((JsonParse.CLa)::(JsonParse.CReturn)::(JsonParse.CNewline)::(JsonParse.CLb)::[])) (((JsonParse.CLa)::[])::((JsonParse.CLb)::[])::[])))})::({tname = "strings-field-reads-an-array-of-strings"; tholds = twin_strings})::({tname = "actor-field-reads-a-human"; tholds = twin_human})::({tname = "from-jsonl-loads-what-try-to-jsonl-wrote"; tholds = twin_round_trip})::({tname = "a-fault-on-the-second-line-names-it"; tholds = twin_second_line})::[]




