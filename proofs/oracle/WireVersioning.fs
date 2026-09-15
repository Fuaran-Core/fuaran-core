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


let bump : profile  ->  evolution  ->  profile = (fun ( base_profile  :  profile ) ( ev  :  evolution ) -> (match (ev) with
| Additive ([]) -> begin
     base_profile
     end
| Additive (uu___) -> begin
     {name = base_profile.name; major = base_profile.major; minor = (base_profile.minor + (Prims.parse_int "1"))}
     end
| Breaking (uu___, uu___1) -> begin
     {name = base_profile.name; major = (base_profile.major + (Prims.parse_int "1")); minor = (Prims.parse_int "0")}
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




