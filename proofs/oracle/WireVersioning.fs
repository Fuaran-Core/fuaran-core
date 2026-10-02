module WireVersioning

let rec diff : Prims.list<Prims.list<WireCanon.ch>>  ->  Prims.list<Prims.list<WireCanon.ch>>  ->  Prims.list<Prims.list<WireCanon.ch>> = (fun ( xs  :  Prims.list<Prims.list<WireCanon.ch>> ) ( ys  :  Prims.list<Prims.list<WireCanon.ch>> ) -> (match (xs) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (WireCanon.mem h ys) then begin
     (diff t ys)
     end else begin
     (h)::(diff t ys)
     end
     end))

type evolution =
| Additive of Prims.list<Prims.list<WireCanon.ch>>
| Breaking of Prims.list<Prims.list<WireCanon.ch>> * Prims.list<Prims.list<WireCanon.ch>>


let uu___is_Additive : evolution  ->  Prims.bool = (fun ( projectee  :  evolution ) -> (match (projectee) with
| Additive (added) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Additive__item__added : evolution  ->  Prims.list<Prims.list<WireCanon.ch>> = (fun ( projectee  :  evolution ) -> (match (projectee) with
| Additive (added) -> begin
     added
     end))


let uu___is_Breaking : evolution  ->  Prims.bool = (fun ( projectee  :  evolution ) -> (match (projectee) with
| Breaking (removed, added) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Breaking__item__removed : evolution  ->  Prims.list<Prims.list<WireCanon.ch>> = (fun ( projectee  :  evolution ) -> (match (projectee) with
| Breaking (removed, added) -> begin
     removed
     end))


let __proj__Breaking__item__added : evolution  ->  Prims.list<Prims.list<WireCanon.ch>> = (fun ( projectee  :  evolution ) -> (match (projectee) with
| Breaking (removed, added) -> begin
     added
     end))


let classify : Prims.list<Prims.list<WireCanon.ch>>  ->  Prims.list<Prims.list<WireCanon.ch>>  ->  evolution = (fun ( before  :  Prims.list<Prims.list<WireCanon.ch>> ) ( after  :  Prims.list<Prims.list<WireCanon.ch>> ) -> (

let added = (diff after before)
in (

let removed = (diff before after)
in  
if (match (removed) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     Additive (added)
     end else begin
     Breaking (removed, added)
     end)))

type profile = {name : Prims.list<WireCanon.ch>; major : Prims.nat; minor : Prims.nat}


let __proj__Mkprofile__item__name : profile  ->  Prims.list<WireCanon.ch> = (fun ( projectee  :  profile ) -> (match (projectee) with
| {name = name; major = major; minor = minor} -> begin
     name
     end))


let __proj__Mkprofile__item__major : profile  ->  Prims.nat = (fun ( projectee  :  profile ) -> (match (projectee) with
| {name = name; major = major; minor = minor} -> begin
     major
     end))


let __proj__Mkprofile__item__minor : profile  ->  Prims.nat = (fun ( projectee  :  profile ) -> (match (projectee) with
| {name = name; major = major; minor = minor} -> begin
     minor
     end))


let max_counter : Prims.nat = (Prims.parse_int "2147483647")


let in_range : profile  ->  Prims.bool = (fun ( p  :  profile ) -> ((p.major <= max_counter) && (p.minor <= max_counter)))

type compatibility =
| Current
| Behind of profile
| Foreign of profile


let uu___is_Current : compatibility  ->  Prims.bool = (fun ( projectee  :  compatibility ) -> (match (projectee) with
| Current -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Behind : compatibility  ->  Prims.bool = (fun ( projectee  :  compatibility ) -> (match (projectee) with
| Behind (authored) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Behind__item__authored : compatibility  ->  profile = (fun ( projectee  :  compatibility ) -> (match (projectee) with
| Behind (authored) -> begin
     authored
     end))


let uu___is_Foreign : compatibility  ->  Prims.bool = (fun ( projectee  :  compatibility ) -> (match (projectee) with
| Foreign (authored) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Foreign__item__authored : compatibility  ->  profile = (fun ( projectee  :  compatibility ) -> (match (projectee) with
| Foreign (authored) -> begin
     authored
     end))


let negotiate : profile  ->  profile  ->  compatibility = (fun ( consumer  :  profile ) ( authored  :  profile ) ->  
if ((Prims.op_Less_Greater authored.name consumer.name) || (Prims.op_Less_Greater authored.major consumer.major)) then begin
     Foreign (authored)
     end else begin
      
if (authored.minor > consumer.minor) then begin
     Behind (authored)
     end else begin
     Current
     end
     end)


let try_bump : profile  ->  evolution  ->  FStar_Pervasives_Native.option<profile> = (fun ( base_profile  :  profile ) ( ev  :  evolution ) -> (match (ev) with
| Additive ([]) -> begin
     FStar_Pervasives_Native.Some (base_profile)
     end
| Additive (uu___) -> begin
      
if (Prims.op_Equals base_profile.minor max_counter) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some ({name = base_profile.name; major = base_profile.major; minor = (base_profile.minor + (Prims.parse_int "1"))})
     end
     end
| Breaking (uu___, uu___1) -> begin
      
if (Prims.op_Equals base_profile.major max_counter) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some ({name = base_profile.name; major = (base_profile.major + (Prims.parse_int "1")); minor = (Prims.parse_int "0")})
     end
     end))


let bump : profile  ->  evolution  ->  profile = (fun ( base_profile  :  profile ) ( ev  :  evolution ) -> (match ((try_bump base_profile ev)) with
| FStar_Pervasives_Native.Some (p) -> begin
     p
     end
| FStar_Pervasives_Native.None -> begin
     base_profile
     end))

type unknown_kind<'num, 'flt> = {kind : Prims.list<WireCanon.ch>; payload : WireCanon.jval<'num, 'flt>; required_profile : FStar_Pervasives_Native.option<profile>}


let __proj__Mkunknown_kind__item__kind = (fun ( projectee  :  unknown_kind<'num, 'flt> ) -> (match (projectee) with
| {kind = kind; payload = payload; required_profile = required_profile} -> begin
     kind
     end))


let __proj__Mkunknown_kind__item__payload = (fun ( projectee  :  unknown_kind<'num, 'flt> ) -> (match (projectee) with
| {kind = kind; payload = payload; required_profile = required_profile} -> begin
     payload
     end))


let __proj__Mkunknown_kind__item__required_profile = (fun ( projectee  :  unknown_kind<'num, 'flt> ) -> (match (projectee) with
| {kind = kind; payload = payload; required_profile = required_profile} -> begin
     required_profile
     end))

type decoded<'num, 'flt, 't> =
| Known of 't
| Unknown of unknown_kind<'num, 'flt>


let uu___is_Known = (fun ( projectee  :  decoded<'num, 'flt, 't> ) -> (match (projectee) with
| Known (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Known__item__v = (fun ( projectee  :  decoded<'num, 'flt, 't> ) -> (match (projectee) with
| Known (v) -> begin
     v
     end))


let uu___is_Unknown = (fun ( projectee  :  decoded<'num, 'flt, 't> ) -> (match (projectee) with
| Unknown (u) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Unknown__item__u = (fun ( projectee  :  decoded<'num, 'flt, 't> ) -> (match (projectee) with
| Unknown (u) -> begin
     u
     end))


let known_in : Prims.list<Prims.list<WireCanon.ch>>  ->  Prims.list<WireCanon.ch>  ->  Prims.bool = (fun ( v  :  Prims.list<Prims.list<WireCanon.ch>> ) ( x  :  Prims.list<WireCanon.ch> ) -> (WireCanon.mem x v))


let decode_tolerant = (fun ( tag_of  :  WireCanon.jval<'num, 'flt>  ->  WireCanon.outcome<Prims.list<WireCanon.ch>> ) ( is_known  :  Prims.list<WireCanon.ch>  ->  Prims.bool ) ( decode_known  :  WireCanon.jval<'num, 'flt>  ->  WireCanon.outcome<'t> ) ( read_required  :  WireCanon.jval<'num, 'flt>  ->  FStar_Pervasives_Native.option<profile> ) ( el  :  WireCanon.jval<'num, 'flt> ) -> (match ((tag_of el)) with
| WireCanon.Error (e) -> begin
     WireCanon.Error (e)
     end
| WireCanon.Ok (tag) -> begin
      
if (is_known tag) then begin
     (match ((decode_known el)) with
| WireCanon.Ok (v) -> begin
     WireCanon.Ok (Known (v))
     end
| WireCanon.Error (e) -> begin
     WireCanon.Error (e)
     end)
     end else begin
     WireCanon.Ok (Unknown ({kind = tag; payload = el; required_profile = (read_required el)}))
     end
     end))


let reencode = (fun ( encode_known  :  't  ->  WireCanon.jval<'num, 'flt> ) ( d  :  decoded<'num, 'flt, 't> ) -> (match (d) with
| Known (v) -> begin
     (encode_known v)
     end
| Unknown (u) -> begin
     u.payload
     end))


let classify_ignoring_removals : Prims.list<Prims.list<WireCanon.ch>>  ->  Prims.list<Prims.list<WireCanon.ch>>  ->  evolution = (fun ( before  :  Prims.list<Prims.list<WireCanon.ch>> ) ( after  :  Prims.list<Prims.list<WireCanon.ch>> ) -> Additive ((diff after before)))

type severity =
| SAdditive
| SBreakingForEmitters
| SBreakingWire
| SHostSurfaceOnly
| SUnclassifiable


let uu___is_SAdditive : severity  ->  Prims.bool = (fun ( projectee  :  severity ) -> (match (projectee) with
| SAdditive -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SBreakingForEmitters : severity  ->  Prims.bool = (fun ( projectee  :  severity ) -> (match (projectee) with
| SBreakingForEmitters -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SBreakingWire : severity  ->  Prims.bool = (fun ( projectee  :  severity ) -> (match (projectee) with
| SBreakingWire -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SHostSurfaceOnly : severity  ->  Prims.bool = (fun ( projectee  :  severity ) -> (match (projectee) with
| SHostSurfaceOnly -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SUnclassifiable : severity  ->  Prims.bool = (fun ( projectee  :  severity ) -> (match (projectee) with
| SUnclassifiable -> begin
     true
     end
| uu___ -> begin
     false
     end))


let required_chars : Prims.list<WireCanon.ch> = (WireCanon.CPlain ("r"))::(WireCanon.CHexCh (WireCanon.HDe))::(WireCanon.CPlain ("q"))::(WireCanon.CLu)::(WireCanon.CPlain ("i"))::(WireCanon.CPlain ("r"))::(WireCanon.CHexCh (WireCanon.HDe))::(WireCanon.CHexCh (WireCanon.HDd))::[]


let host_only_chars : Prims.list<WireCanon.ch> = (WireCanon.CPlain ("h"))::(WireCanon.CPlain ("o"))::(WireCanon.CPlain ("s"))::(WireCanon.CPlain ("t"))::(WireCanon.CPlain ("O"))::(WireCanon.CPlain ("n"))::(WireCanon.CPlain ("l"))::(WireCanon.CPlain ("y"))::[]


let classify_field_add : Prims.list<WireCanon.ch>  ->  severity = (fun ( opt_class  :  Prims.list<WireCanon.ch> ) ->  
if (Prims.op_Equals opt_class required_chars) then begin
     SBreakingWire
     end else begin
      
if (Prims.op_Equals opt_class host_only_chars) then begin
     SHostSurfaceOnly
     end else begin
     SAdditive
     end
     end)


let retires : severity  ->  Prims.bool = (fun ( s  :  severity ) -> (match (s) with
| SBreakingWire -> begin
     true
     end
| uu___ -> begin
     false
     end))


let introduces : severity  ->  Prims.bool = (fun ( s  :  severity ) -> ((match (s) with
| SAdditive -> begin
     true
     end
| uu___ -> begin
     false
     end) || (match (s) with
| SBreakingForEmitters -> begin
     true
     end
| uu___ -> begin
     false
     end)))


let rec subjects : (severity  ->  Prims.bool)  ->  Prims.list<(severity * Prims.list<WireCanon.ch>)>  ->  Prims.list<Prims.list<WireCanon.ch>> = (fun ( p  :  severity  ->  Prims.bool ) ( rows  :  Prims.list<(severity * Prims.list<WireCanon.ch>)> ) -> (match (rows) with
| [] -> begin
     []
     end
| ((s, subj))::t -> begin
      
if (p s) then begin
     (subj)::(subjects p t)
     end else begin
     (subjects p t)
     end
     end))


let evolution_of : Prims.list<(severity * Prims.list<WireCanon.ch>)>  ->  evolution = (fun ( rows  :  Prims.list<(severity * Prims.list<WireCanon.ch>)> ) -> (classify (subjects retires rows) (subjects introduces rows)))


let classify_field_add_ignoring_optionality : Prims.list<WireCanon.ch>  ->  severity = (fun ( opt_class  :  Prims.list<WireCanon.ch> ) -> SAdditive)


let rec app = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     m
     end
| (h)::t -> begin
     (h)::(app t m)
     end))


type field_decl = (Prims.list<WireCanon.ch> * Prims.list<WireCanon.ch>)


let rec member_of = (fun ( k  :  Prims.list<WireCanon.ch> ) ( ms  :  Prims.list<(Prims.list<WireCanon.ch> * WireCanon.jval<'num, 'flt>)> ) -> (match (ms) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((k', v))::t -> begin
      
if (Prims.op_Equals k' k) then begin
     FStar_Pervasives_Native.Some (v)
     end else begin
     (member_of k t)
     end
     end))


let rec decode_fields = (fun ( vocab  :  Prims.list<field_decl> ) ( ms  :  Prims.list<(Prims.list<WireCanon.ch> * WireCanon.jval<'num, 'flt>)> ) -> (match (vocab) with
| [] -> begin
     WireCanon.Ok ([])
     end
| ((n, c))::rest -> begin
      
if (Prims.op_Equals c host_only_chars) then begin
     (decode_fields rest ms)
     end else begin
     (match ((member_of n ms)) with
| FStar_Pervasives_Native.None -> begin
      
if (Prims.op_Equals c required_chars) then begin
     WireCanon.Error ("required field is absent")
     end else begin
     (decode_fields rest ms)
     end
     end
| FStar_Pervasives_Native.Some (v) -> begin
     (match ((decode_fields rest ms)) with
| WireCanon.Ok (r) -> begin
     WireCanon.Ok ((((n), (v)))::r)
     end
| WireCanon.Error (e) -> begin
     WireCanon.Error (e)
     end)
     end)
     end
     end))


let decode_known_in = (fun ( vocab  :  Prims.list<field_decl> ) ( el  :  WireCanon.jval<'num, 'flt> ) -> (match (el) with
| WireCanon.JObj (ms) -> begin
     (decode_fields vocab ms)
     end
| uu___ -> begin
     WireCanon.Error ("expected an object")
     end))


let carries = (fun ( k  :  Prims.list<WireCanon.ch> ) ( el  :  WireCanon.jval<'num, 'flt> ) -> (match (el) with
| WireCanon.JObj (ms) -> begin
     (match ((member_of k ms)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| uu___ -> begin
     false
     end))


let classify_field_add_pre_304 : Prims.list<WireCanon.ch>  ->  severity = (fun ( opt_class  :  Prims.list<WireCanon.ch> ) ->  
if (Prims.op_Equals opt_class required_chars) then begin
     SBreakingForEmitters
     end else begin
      
if (Prims.op_Equals opt_class host_only_chars) then begin
     SHostSurfaceOnly
     end else begin
     SAdditive
     end
     end)

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


let twins : Prims.list<twin> = ({tname = "classify-a-removal-is-breaking"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (classify (((WireCanon.CPlain ("a"))::[])::((WireCanon.CPlain ("b"))::[])::[]) (((WireCanon.CPlain ("a"))::[])::((WireCanon.CPlain ("c"))::[])::[])) (Breaking (((WireCanon.CPlain ("b"))::[])::[], ((WireCanon.CPlain ("c"))::[])::[]))))})::({tname = "classify-an-addition-is-additive"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (classify (((WireCanon.CPlain ("a"))::[])::[]) (((WireCanon.CPlain ("a"))::[])::((WireCanon.CPlain ("c"))::[])::[])) (Additive (((WireCanon.CPlain ("c"))::[])::[]))))})::({tname = "a-required-field-breaks-the-wire"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (classify_field_add required_chars) SBreakingWire))})::({tname = "an-old-document-is-refused-under-an-added-required-field"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (decode_known_in (((((WireCanon.CPlain ("c"))::[]), (required_chars)))::[]) (WireCanon.JObj ([]))) (WireCanon.Error ("required field is absent"))))})::({tname = "an-old-document-is-unchanged-under-an-added-optional-field"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (decode_known_in (((((WireCanon.CPlain ("c"))::[]), ((WireCanon.CPlain ("o"))::[])))::[]) (WireCanon.JObj (((((WireCanon.CPlain ("b"))::[]), (WireCanon.JInt ((Prims.parse_int "1")))))::[]))) (WireCanon.Ok ([]))))})::[]




