module Capability
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


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let rec app = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     m
     end
| (x)::t -> begin
     (x)::(app t m)
     end))


let rec rev = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (app (rev t) ((x)::[]))
     end))


let rec map = (fun ( f  :  'a  ->  'b ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((f x))::(map f t)
     end))


let rec filter = (fun ( p  :  'a  ->  Prims.bool ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (p x) then begin
     (x)::(filter p t)
     end else begin
     (filter p t)
     end
     end))


let rec for_all = (fun ( p  :  'a  ->  Prims.bool ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((p x) && (for_all p t))
     end))


let rec try_find = (fun ( p  :  'a  ->  Prims.bool ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::t -> begin
      
if (p x) then begin
     FStar_Pervasives_Native.Some (x)
     end else begin
     (try_find p t)
     end
     end))


let rec try_pick = (fun ( f  :  'a  ->  FStar_Pervasives_Native.option<'b> ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::t -> begin
     (match ((f x)) with
| FStar_Pervasives_Native.Some (y) -> begin
     FStar_Pervasives_Native.Some (y)
     end
| FStar_Pervasives_Native.None -> begin
     (try_pick f t)
     end)
     end))


let rec fold_left = (fun ( f  :  'b  ->  'a  ->  'b ) ( acc  :  'b ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     acc
     end
| (x)::t -> begin
     (fold_left f (f acc x) t)
     end))


let rec mem : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( x  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals x y) || (mem x t))
     end))


let rec assoc = (fun ( k  :  Prims.string ) ( l  :  Prims.list<(Prims.string * 'a)> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((k', v))::t -> begin
      
if (Prims.op_Equals k k') then begin
     FStar_Pervasives_Native.Some (v)
     end else begin
     (assoc k t)
     end
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


let rec distinct : Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((not ((mem x t))) && (distinct t))
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

type determinism_source =
| Deterministic
| Clock
| Random
| Network


let uu___is_Deterministic : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| Deterministic -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Clock : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| Clock -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Random : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| Random -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Network : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| Network -> begin
     true
     end
| uu___ -> begin
     false
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


let pure_deterministic : effect_class = {host = Pure; determinism = Deterministic}


let host_rank : host_effect  ->  Prims.int = (fun ( h  :  host_effect ) -> (match (h) with
| Pure -> begin
     (Prims.parse_int "0")
     end
| ReadsHost -> begin
     (Prims.parse_int "1")
     end
| WritesHost -> begin
     (Prims.parse_int "2")
     end))


let det_rank : determinism_source  ->  Prims.int = (fun ( d  :  determinism_source ) -> (match (d) with
| Deterministic -> begin
     (Prims.parse_int "0")
     end
| Clock -> begin
     (Prims.parse_int "1")
     end
| Random -> begin
     (Prims.parse_int "2")
     end
| Network -> begin
     (Prims.parse_int "3")
     end))


let host_of : Prims.int  ->  host_effect = (fun ( n  :  Prims.int ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     Pure
     end else begin
      
if (Prims.op_Equals n (Prims.parse_int "1")) then begin
     ReadsHost
     end else begin
     WritesHost
     end
     end)


let det_of : Prims.int  ->  determinism_source = (fun ( n  :  Prims.int ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     Deterministic
     end else begin
      
if (Prims.op_Equals n (Prims.parse_int "1")) then begin
     Clock
     end else begin
      
if (Prims.op_Equals n (Prims.parse_int "2")) then begin
     Random
     end else begin
     Network
     end
     end
     end)


let max_int : Prims.int  ->  Prims.int  ->  Prims.int = (fun ( a  :  Prims.int ) ( b  :  Prims.int ) ->  
if (a >= b) then begin
     a
     end else begin
     b
     end)


let join : effect_class  ->  effect_class  ->  effect_class = (fun ( a  :  effect_class ) ( b  :  effect_class ) -> {host = (host_of (max_int (host_rank a.host) (host_rank b.host))); determinism = (det_of (max_int (det_rank a.determinism) (det_rank b.determinism)))})


let covers : effect_class  ->  effect_class  ->  Prims.bool = (fun ( declared  :  effect_class ) ( actual  :  effect_class ) -> (((host_rank declared.host) >= (host_rank actual.host)) && ((det_rank declared.determinism) >= (det_rank actual.determinism))))


let determinism_tag : determinism_source  ->  Prims.string = (fun ( d  :  determinism_source ) -> (match (d) with
| Deterministic -> begin
     "deterministic"
     end
| Clock -> begin
     "clock"
     end
| Random -> begin
     "random"
     end
| Network -> begin
     "network"
     end))

type value_space =
| IntRange of Prims.int * Prims.int
| FloatRange of Prims.string * Prims.string
| StringLen of Prims.int * Prims.int
| Enum of Prims.list<Prims.string>
| AnyString


let uu___is_IntRange : value_space  ->  Prims.bool = (fun ( projectee  :  value_space ) -> (match (projectee) with
| IntRange (lo, hi) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__IntRange__item__lo : value_space  ->  Prims.int = (fun ( projectee  :  value_space ) -> (match (projectee) with
| IntRange (lo, hi) -> begin
     lo
     end))


let __proj__IntRange__item__hi : value_space  ->  Prims.int = (fun ( projectee  :  value_space ) -> (match (projectee) with
| IntRange (lo, hi) -> begin
     hi
     end))


let uu___is_FloatRange : value_space  ->  Prims.bool = (fun ( projectee  :  value_space ) -> (match (projectee) with
| FloatRange (lo, hi) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__FloatRange__item__lo : value_space  ->  Prims.string = (fun ( projectee  :  value_space ) -> (match (projectee) with
| FloatRange (lo, hi) -> begin
     lo
     end))


let __proj__FloatRange__item__hi : value_space  ->  Prims.string = (fun ( projectee  :  value_space ) -> (match (projectee) with
| FloatRange (lo, hi) -> begin
     hi
     end))


let uu___is_StringLen : value_space  ->  Prims.bool = (fun ( projectee  :  value_space ) -> (match (projectee) with
| StringLen (lo, hi) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__StringLen__item__lo : value_space  ->  Prims.int = (fun ( projectee  :  value_space ) -> (match (projectee) with
| StringLen (lo, hi) -> begin
     lo
     end))


let __proj__StringLen__item__hi : value_space  ->  Prims.int = (fun ( projectee  :  value_space ) -> (match (projectee) with
| StringLen (lo, hi) -> begin
     hi
     end))


let uu___is_Enum : value_space  ->  Prims.bool = (fun ( projectee  :  value_space ) -> (match (projectee) with
| Enum (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Enum__item___0 : value_space  ->  Prims.list<Prims.string> = (fun ( projectee  :  value_space ) -> (match (projectee) with
| Enum (_0) -> begin
     _0
     end))


let uu___is_AnyString : value_space  ->  Prims.bool = (fun ( projectee  :  value_space ) -> (match (projectee) with
| AnyString -> begin
     true
     end
| uu___ -> begin
     false
     end))

type readers = {int_of : Prims.string  ->  FStar_Pervasives_Native.option<Prims.int>; float_in : Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.bool; str_len : Prims.string  ->  Prims.nat}


let __proj__Mkreaders__item__int_of : readers  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  readers ) -> (match (projectee) with
| {int_of = int_of; float_in = float_in; str_len = str_len} -> begin
     int_of
     end))


let __proj__Mkreaders__item__float_in : readers  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( projectee  :  readers ) -> (match (projectee) with
| {int_of = int_of; float_in = float_in; str_len = str_len} -> begin
     float_in
     end))


let __proj__Mkreaders__item__str_len : readers  ->  Prims.string  ->  Prims.nat = (fun ( projectee  :  readers ) -> (match (projectee) with
| {int_of = int_of; float_in = float_in; str_len = str_len} -> begin
     str_len
     end))


let validate : readers  ->  value_space  ->  Prims.string  ->  Prims.bool = (fun ( rd  :  readers ) ( space  :  value_space ) ( s  :  Prims.string ) -> (match (space) with
| IntRange (lo, hi) -> begin
     (match ((rd.int_of s)) with
| FStar_Pervasives_Native.Some (v) -> begin
     ((v >= lo) && (v <= hi))
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)
     end
| FloatRange (lo, hi) -> begin
     (rd.float_in lo hi s)
     end
| StringLen (lo, hi) -> begin
     (((rd.str_len s) >= lo) && ((rd.str_len s) <= hi))
     end
| Enum (xs) -> begin
     (mem s xs)
     end
| AnyString -> begin
     true
     end))


let is_bounded : value_space  ->  Prims.bool = (fun ( space  :  value_space ) -> (match (space) with
| AnyString -> begin
     false
     end
| uu___ -> begin
     true
     end))

type hole_kind =
| ValueHole of value_space
| SlotHole of FStar_Pervasives_Native.option<Prims.string>
| RepeatHole of value_space
| ActionHole of effect_class


let uu___is_ValueHole : hole_kind  ->  Prims.bool = (fun ( projectee  :  hole_kind ) -> (match (projectee) with
| ValueHole (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ValueHole__item___0 : hole_kind  ->  value_space = (fun ( projectee  :  hole_kind ) -> (match (projectee) with
| ValueHole (_0) -> begin
     _0
     end))


let uu___is_SlotHole : hole_kind  ->  Prims.bool = (fun ( projectee  :  hole_kind ) -> (match (projectee) with
| SlotHole (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SlotHole__item___0 : hole_kind  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( projectee  :  hole_kind ) -> (match (projectee) with
| SlotHole (_0) -> begin
     _0
     end))


let uu___is_RepeatHole : hole_kind  ->  Prims.bool = (fun ( projectee  :  hole_kind ) -> (match (projectee) with
| RepeatHole (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RepeatHole__item___0 : hole_kind  ->  value_space = (fun ( projectee  :  hole_kind ) -> (match (projectee) with
| RepeatHole (_0) -> begin
     _0
     end))


let uu___is_ActionHole : hole_kind  ->  Prims.bool = (fun ( projectee  :  hole_kind ) -> (match (projectee) with
| ActionHole (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ActionHole__item___0 : hole_kind  ->  effect_class = (fun ( projectee  :  hole_kind ) -> (match (projectee) with
| ActionHole (_0) -> begin
     _0
     end))

type hole_decl = {h_addr : Prims.string; h_name : Prims.string; h_kind : hole_kind}


let __proj__Mkhole_decl__item__h_addr : hole_decl  ->  Prims.string = (fun ( projectee  :  hole_decl ) -> (match (projectee) with
| {h_addr = h_addr; h_name = h_name; h_kind = h_kind} -> begin
     h_addr
     end))


let __proj__Mkhole_decl__item__h_name : hole_decl  ->  Prims.string = (fun ( projectee  :  hole_decl ) -> (match (projectee) with
| {h_addr = h_addr; h_name = h_name; h_kind = h_kind} -> begin
     h_name
     end))


let __proj__Mkhole_decl__item__h_kind : hole_decl  ->  hole_kind = (fun ( projectee  :  hole_decl ) -> (match (projectee) with
| {h_addr = h_addr; h_name = h_name; h_kind = h_kind} -> begin
     h_kind
     end))

type sig_entry = {s_addr : Prims.string; s_name : Prims.string; s_kind : Prims.string; s_space : FStar_Pervasives_Native.option<value_space>; s_slot : FStar_Pervasives_Native.option<Prims.string>; s_action : FStar_Pervasives_Native.option<effect_class>; s_required : Prims.bool}


let __proj__Mksig_entry__item__s_addr : sig_entry  ->  Prims.string = (fun ( projectee  :  sig_entry ) -> (match (projectee) with
| {s_addr = s_addr; s_name = s_name; s_kind = s_kind; s_space = s_space; s_slot = s_slot; s_action = s_action; s_required = s_required} -> begin
     s_addr
     end))


let __proj__Mksig_entry__item__s_name : sig_entry  ->  Prims.string = (fun ( projectee  :  sig_entry ) -> (match (projectee) with
| {s_addr = s_addr; s_name = s_name; s_kind = s_kind; s_space = s_space; s_slot = s_slot; s_action = s_action; s_required = s_required} -> begin
     s_name
     end))


let __proj__Mksig_entry__item__s_kind : sig_entry  ->  Prims.string = (fun ( projectee  :  sig_entry ) -> (match (projectee) with
| {s_addr = s_addr; s_name = s_name; s_kind = s_kind; s_space = s_space; s_slot = s_slot; s_action = s_action; s_required = s_required} -> begin
     s_kind
     end))


let __proj__Mksig_entry__item__s_space : sig_entry  ->  FStar_Pervasives_Native.option<value_space> = (fun ( projectee  :  sig_entry ) -> (match (projectee) with
| {s_addr = s_addr; s_name = s_name; s_kind = s_kind; s_space = s_space; s_slot = s_slot; s_action = s_action; s_required = s_required} -> begin
     s_space
     end))


let __proj__Mksig_entry__item__s_slot : sig_entry  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( projectee  :  sig_entry ) -> (match (projectee) with
| {s_addr = s_addr; s_name = s_name; s_kind = s_kind; s_space = s_space; s_slot = s_slot; s_action = s_action; s_required = s_required} -> begin
     s_slot
     end))


let __proj__Mksig_entry__item__s_action : sig_entry  ->  FStar_Pervasives_Native.option<effect_class> = (fun ( projectee  :  sig_entry ) -> (match (projectee) with
| {s_addr = s_addr; s_name = s_name; s_kind = s_kind; s_space = s_space; s_slot = s_slot; s_action = s_action; s_required = s_required} -> begin
     s_action
     end))


let __proj__Mksig_entry__item__s_required : sig_entry  ->  Prims.bool = (fun ( projectee  :  sig_entry ) -> (match (projectee) with
| {s_addr = s_addr; s_name = s_name; s_kind = s_kind; s_space = s_space; s_slot = s_slot; s_action = s_action; s_required = s_required} -> begin
     s_required
     end))

type signature = {sg_name : Prims.string; sg_holes : Prims.list<sig_entry>; sg_effect : effect_class}


let __proj__Mksignature__item__sg_name : signature  ->  Prims.string = (fun ( projectee  :  signature ) -> (match (projectee) with
| {sg_name = sg_name; sg_holes = sg_holes; sg_effect = sg_effect} -> begin
     sg_name
     end))


let __proj__Mksignature__item__sg_holes : signature  ->  Prims.list<sig_entry> = (fun ( projectee  :  signature ) -> (match (projectee) with
| {sg_name = sg_name; sg_holes = sg_holes; sg_effect = sg_effect} -> begin
     sg_holes
     end))


let __proj__Mksignature__item__sg_effect : signature  ->  effect_class = (fun ( projectee  :  signature ) -> (match (projectee) with
| {sg_name = sg_name; sg_holes = sg_holes; sg_effect = sg_effect} -> begin
     sg_effect
     end))

type arg<'node> =
| ValueArg of Prims.string
| SlotArg of 'node


let uu___is_ValueArg = (fun ( projectee  :  arg<'node> ) -> (match (projectee) with
| ValueArg (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ValueArg__item___0 = (fun ( projectee  :  arg<'node> ) -> (match (projectee) with
| ValueArg (_0) -> begin
     _0
     end))


let uu___is_SlotArg = (fun ( projectee  :  arg<'node> ) -> (match (projectee) with
| SlotArg (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SlotArg__item___0 = (fun ( projectee  :  arg<'node> ) -> (match (projectee) with
| SlotArg (_0) -> begin
     _0
     end))

type apply_error =
| UnknownHoleAddr of Prims.string * Prims.list<Prims.string>
| ValueOutOfSpace of Prims.string * value_space * Prims.string
| RequiredHolesUnbound of Prims.list<Prims.string>
| NotASlot of Prims.string
| SlotKindMismatch of Prims.string * Prims.string * Prims.string
| NonTotal of Prims.string
| BindFailed of Prims.string * Prims.string


let uu___is_UnknownHoleAddr : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| UnknownHoleAddr (addr, declared) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownHoleAddr__item__addr : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| UnknownHoleAddr (addr, declared) -> begin
     addr
     end))


let __proj__UnknownHoleAddr__item__declared : apply_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| UnknownHoleAddr (addr, declared) -> begin
     declared
     end))


let uu___is_ValueOutOfSpace : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| ValueOutOfSpace (addr, space, got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ValueOutOfSpace__item__addr : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| ValueOutOfSpace (addr, space, got) -> begin
     addr
     end))


let __proj__ValueOutOfSpace__item__space : apply_error  ->  value_space = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| ValueOutOfSpace (addr, space, got) -> begin
     space
     end))


let __proj__ValueOutOfSpace__item__got : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| ValueOutOfSpace (addr, space, got) -> begin
     got
     end))


let uu___is_RequiredHolesUnbound : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| RequiredHolesUnbound (addrs) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RequiredHolesUnbound__item__addrs : apply_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| RequiredHolesUnbound (addrs) -> begin
     addrs
     end))


let uu___is_NotASlot : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| NotASlot (addr) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NotASlot__item__addr : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| NotASlot (addr) -> begin
     addr
     end))


let uu___is_SlotKindMismatch : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| SlotKindMismatch (addr, expected, got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SlotKindMismatch__item__addr : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| SlotKindMismatch (addr, expected, got) -> begin
     addr
     end))


let __proj__SlotKindMismatch__item__expected : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| SlotKindMismatch (addr, expected, got) -> begin
     expected
     end))


let __proj__SlotKindMismatch__item__got : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| SlotKindMismatch (addr, expected, got) -> begin
     got
     end))


let uu___is_NonTotal : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| NonTotal (addr) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NonTotal__item__addr : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| NonTotal (addr) -> begin
     addr
     end))


let uu___is_BindFailed : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| BindFailed (addr, reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__BindFailed__item__addr : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| BindFailed (addr, reason) -> begin
     addr
     end))


let __proj__BindFailed__item__reason : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| BindFailed (addr, reason) -> begin
     reason
     end))

type invoke_error =
| NoSuchCapability of Prims.string * Prims.list<Prims.string>
| DuplicateCapability of Prims.string
| UnknownArg of Prims.string * Prims.list<Prims.string>
| ArgOutOfSpace of Prims.string * value_space * Prims.string
| RequiredArgsUnbound of Prims.list<Prims.string>
| UninvocableArg of Prims.string
| BodyFailed of Prims.string


let uu___is_NoSuchCapability : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| NoSuchCapability (id, known) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NoSuchCapability__item__id : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| NoSuchCapability (id, known) -> begin
     id
     end))


let __proj__NoSuchCapability__item__known : invoke_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| NoSuchCapability (id, known) -> begin
     known
     end))


let uu___is_DuplicateCapability : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| DuplicateCapability (id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateCapability__item__id : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| DuplicateCapability (id) -> begin
     id
     end))


let uu___is_UnknownArg : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| UnknownArg (addr, declared) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownArg__item__addr : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| UnknownArg (addr, declared) -> begin
     addr
     end))


let __proj__UnknownArg__item__declared : invoke_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| UnknownArg (addr, declared) -> begin
     declared
     end))


let uu___is_ArgOutOfSpace : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| ArgOutOfSpace (addr, space, got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ArgOutOfSpace__item__addr : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| ArgOutOfSpace (addr, space, got) -> begin
     addr
     end))


let __proj__ArgOutOfSpace__item__space : invoke_error  ->  value_space = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| ArgOutOfSpace (addr, space, got) -> begin
     space
     end))


let __proj__ArgOutOfSpace__item__got : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| ArgOutOfSpace (addr, space, got) -> begin
     got
     end))


let uu___is_RequiredArgsUnbound : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| RequiredArgsUnbound (addrs) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RequiredArgsUnbound__item__addrs : invoke_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| RequiredArgsUnbound (addrs) -> begin
     addrs
     end))


let uu___is_UninvocableArg : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| UninvocableArg (addr) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UninvocableArg__item__addr : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| UninvocableArg (addr) -> begin
     addr
     end))


let uu___is_BodyFailed : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| BodyFailed (reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__BodyFailed__item__reason : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| BodyFailed (reason) -> begin
     reason
     end))


let rec find_hole : Prims.string  ->  Prims.list<hole_decl>  ->  FStar_Pervasives_Native.option<hole_decl> = (fun ( k  :  Prims.string ) ( holes  :  Prims.list<hole_decl> ) -> (match (holes) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (h)::t -> begin
      
if (Prims.op_Equals h.h_addr k) then begin
     FStar_Pervasives_Native.Some (h)
     end else begin
     (find_hole k t)
     end
     end))


let rec find_entry : Prims.string  ->  Prims.list<sig_entry>  ->  FStar_Pervasives_Native.option<sig_entry> = (fun ( k  :  Prims.string ) ( holes  :  Prims.list<sig_entry> ) -> (match (holes) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (h)::t -> begin
      
if (Prims.op_Equals h.s_addr k) then begin
     FStar_Pervasives_Native.Some (h)
     end else begin
     (find_entry k t)
     end
     end))


let addr_of : hole_decl  ->  Prims.string = (fun ( h  :  hole_decl ) -> h.h_addr)


let rec entry_addrs : Prims.list<sig_entry>  ->  Prims.list<Prims.string> = (fun ( holes  :  Prims.list<sig_entry> ) -> (match (holes) with
| [] -> begin
     []
     end
| (h)::t -> begin
     (h.s_addr)::(entry_addrs t)
     end))


let rec first_unknown : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( declared  :  Prims.list<Prims.string> ) ( ks  :  Prims.list<Prims.string> ) -> (match (ks) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (k)::t -> begin
      
if (not ((mem k declared))) then begin
     FStar_Pervasives_Native.Some (k)
     end else begin
     (first_unknown declared t)
     end
     end))


let rec excluding : Prims.list<Prims.string>  ->  Prims.list<sig_entry>  ->  Prims.list<sig_entry> = (fun ( bound  :  Prims.list<Prims.string> ) ( holes  :  Prims.list<sig_entry> ) -> (match (holes) with
| [] -> begin
     []
     end
| (e)::t -> begin
      
if (not ((mem e.s_addr bound))) then begin
     (e)::(excluding bound t)
     end else begin
     (excluding bound t)
     end
     end))

type witness<'node> = {holes : 'node  ->  Prims.list<hole_decl>; eff : 'node  ->  effect_class; bind_hole : Prims.string  ->  arg<'node>  ->  'node  ->  outcome<'node, Prims.string>; kind_tag : 'node  ->  Prims.string; preorder : 'node  ->  Prims.list<'node>}


let __proj__Mkwitness__item__holes = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder} -> begin
     holes
     end))


let __proj__Mkwitness__item__eff = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder} -> begin
     eff
     end))


let __proj__Mkwitness__item__bind_hole = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder} -> begin
     bind_hole
     end))


let __proj__Mkwitness__item__kind_tag = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder} -> begin
     kind_tag
     end))


let __proj__Mkwitness__item__preorder = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder} -> begin
     preorder
     end))


let entry_of : hole_decl  ->  sig_entry = (fun ( h  :  hole_decl ) -> (match (h.h_kind) with
| ValueHole (s) -> begin
     {s_addr = h.h_addr; s_name = h.h_name; s_kind = "value"; s_space = FStar_Pervasives_Native.Some (s); s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.None; s_required = true}
     end
| SlotHole (c) -> begin
     {s_addr = h.h_addr; s_name = h.h_name; s_kind = "slot"; s_space = FStar_Pervasives_Native.None; s_slot = c; s_action = FStar_Pervasives_Native.None; s_required = true}
     end
| RepeatHole (s) -> begin
     {s_addr = h.h_addr; s_name = h.h_name; s_kind = "repeat"; s_space = FStar_Pervasives_Native.Some (s); s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.None; s_required = false}
     end
| ActionHole (e) -> begin
     {s_addr = h.h_addr; s_name = h.h_name; s_kind = "action"; s_space = FStar_Pervasives_Native.None; s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.Some (e); s_required = false}
     end))


let signature_of = (fun ( w  :  witness<'node> ) ( name  :  Prims.string ) ( n  :  'node ) -> {sg_name = name; sg_holes = (map entry_of (w.holes n)); sg_effect = (w.eff n)})


let signature_excluding : Prims.list<Prims.string>  ->  signature  ->  signature = (fun ( bound  :  Prims.list<Prims.string> ) ( sg  :  signature ) -> {sg_name = sg.sg_name; sg_holes = (excluding bound sg.sg_holes); sg_effect = sg.sg_effect})


let entry_total : sig_entry  ->  Prims.bool = (fun ( e  :  sig_entry ) -> ((Prims.op_Less_Greater e.s_kind "repeat") || (match (e.s_space) with
| FStar_Pervasives_Native.Some (s) -> begin
     (is_bounded s)
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)))


let is_total : signature  ->  Prims.bool = (fun ( sg  :  signature ) -> (for_all entry_total sg.sg_holes))


let non_total : hole_decl  ->  FStar_Pervasives_Native.option<apply_error> = (fun ( h  :  hole_decl ) -> (match (h.h_kind) with
| RepeatHole (s) -> begin
      
if (not ((is_bounded s))) then begin
     FStar_Pervasives_Native.Some (NonTotal (h.h_addr))
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let guard_total : Prims.list<hole_decl>  ->  FStar_Pervasives_Native.option<apply_error> = (fun ( holes  :  Prims.list<hole_decl> ) -> (try_pick non_total holes))


let validate_arg = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( addr  :  Prims.string ) ( k  :  hole_kind ) ( a  :  arg<'node> ) -> (match (((k), (a))) with
| (ValueHole (space), ValueArg (s)) -> begin
      
if (validate rd space s) then begin
     Ok (())
     end else begin
     Error (ValueOutOfSpace (addr, space, s))
     end
     end
| (RepeatHole (space), ValueArg (s)) -> begin
      
if (not ((is_bounded space))) then begin
     Error (NonTotal (addr))
     end else begin
      
if (validate rd space s) then begin
     Ok (())
     end else begin
     Error (ValueOutOfSpace (addr, space, s))
     end
     end
     end
| (SlotHole (c), SlotArg (inner)) -> begin
     (match (c) with
| FStar_Pervasives_Native.Some (kt) -> begin
      
if (Prims.op_Less_Greater (w.kind_tag inner) kt) then begin
     Error (SlotKindMismatch (addr, kt, (w.kind_tag inner)))
     end else begin
     Ok (())
     end
     end
| FStar_Pervasives_Native.None -> begin
     Ok (())
     end)
     end
| (ValueHole (uu___), SlotArg (uu___1)) -> begin
     Error (NotASlot (addr))
     end
| (RepeatHole (uu___), SlotArg (uu___1)) -> begin
     Error (NotASlot (addr))
     end
| (SlotHole (uu___), ValueArg (uu___1)) -> begin
     Error (NotASlot (addr))
     end
| (ActionHole (uu___), uu___1) -> begin
     Error (NotASlot (addr))
     end))


let is_data : hole_decl  ->  Prims.bool = (fun ( h  :  hole_decl ) -> (match (h.h_kind) with
| ActionHole (uu___) -> begin
     false
     end
| uu___ -> begin
     true
     end))


let data_holes = (fun ( w  :  witness<'node> ) ( n  :  'node ) -> (filter is_data (w.holes n)))


type args<'node> = Prims.list<(Prims.string * arg<'node>)>


let rec bind_walk = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( strict  :  Prims.bool ) ( a  :  args<'node> ) ( cur  :  'node ) ( unbound  :  Prims.list<Prims.string> ) ( holes  :  Prims.list<hole_decl> ) -> (match (holes) with
| [] -> begin
     (match (unbound) with
| [] -> begin
     Ok (cur)
     end
| uu___ -> begin
      
if strict then begin
     Error (RequiredHolesUnbound ((rev unbound)))
     end else begin
     Ok (cur)
     end
     end)
     end
| (h)::rest -> begin
     (match ((assoc h.h_addr a)) with
| FStar_Pervasives_Native.None -> begin
     (bind_walk rd w strict a cur ((h.h_addr)::unbound) rest)
     end
| FStar_Pervasives_Native.Some (x) -> begin
     (match ((validate_arg rd w h.h_addr h.h_kind x)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((w.bind_hole h.h_addr x cur)) with
| Ok (cur') -> begin
     (bind_walk rd w strict a cur' unbound rest)
     end
| Error (m) -> begin
     Error (BindFailed (h.h_addr, m))
     end)
     end)
     end)
     end))


let bind_args = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( strict  :  Prims.bool ) ( a  :  args<'node> ) ( n  :  'node ) -> (

let holes = (data_holes w n)
in (match ((guard_total holes)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     (

let declared = (map addr_of holes)
in (match ((first_unknown declared (keys a))) with
| FStar_Pervasives_Native.Some (unknown) -> begin
     Error (UnknownHoleAddr (unknown, declared))
     end
| FStar_Pervasives_Native.None -> begin
     (bind_walk rd w strict a n [] holes)
     end))
     end)))


let apply = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( a  :  args<'node> ) ( n  :  'node ) -> (bind_args rd w true a n))


let curry = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( a  :  args<'node> ) ( n  :  'node ) -> (bind_args rd w false a n))


let composed_effect = (fun ( w  :  witness<'node> ) ( inner  :  'node ) ( outer  :  'node ) -> (join (w.eff outer) (w.eff inner)))


let wire = (fun ( w  :  witness<'node> ) ( slot_addr  :  Prims.string ) ( inner  :  'node ) ( outer  :  'node ) -> (match ((w.bind_hole slot_addr (SlotArg (inner)) outer)) with
| Ok (n) -> begin
     Ok (n)
     end
| Error (m) -> begin
     Error (BindFailed (slot_addr, m))
     end))


let compose = (fun ( w  :  witness<'node> ) ( slot_addr  :  Prims.string ) ( inner  :  'node ) ( outer  :  'node ) -> (

let holes = (w.holes outer)
in (match ((find_hole slot_addr holes)) with
| FStar_Pervasives_Native.None -> begin
     Error (UnknownHoleAddr (slot_addr, (map addr_of holes)))
     end
| FStar_Pervasives_Native.Some (h) -> begin
     (match (h.h_kind) with
| SlotHole (c) -> begin
     (match (c) with
| FStar_Pervasives_Native.Some (kt) -> begin
      
if (Prims.op_Less_Greater (w.kind_tag inner) kt) then begin
     Error (SlotKindMismatch (slot_addr, kt, (w.kind_tag inner)))
     end else begin
     (wire w slot_addr inner outer)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (wire w slot_addr inner outer)
     end)
     end
| uu___ -> begin
     Error (NotASlot (slot_addr))
     end)
     end)))


let observed_effect = (fun ( w  :  witness<'node> ) ( n  :  'node ) -> (fold_left join pure_deterministic (map w.eff (w.preorder n))))


let audit_effect = (fun ( w  :  witness<'node> ) ( n  :  'node ) -> (

let declared = (w.eff n)
in (

let actual = (observed_effect w n)
in  
if (covers declared actual) then begin
     Ok (())
     end else begin
     Error (((declared), (actual)))
     end)))

type island_kind =
| Pyodide
| Fable
| Js


let uu___is_Pyodide : island_kind  ->  Prims.bool = (fun ( projectee  :  island_kind ) -> (match (projectee) with
| Pyodide -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Fable : island_kind  ->  Prims.bool = (fun ( projectee  :  island_kind ) -> (match (projectee) with
| Fable -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Js : island_kind  ->  Prims.bool = (fun ( projectee  :  island_kind ) -> (match (projectee) with
| Js -> begin
     true
     end
| uu___ -> begin
     false
     end))

type placement =
| BuildTime
| Server
| ClientDeclarative
| ClientIsland of island_kind
| Precomputed


let uu___is_BuildTime : placement  ->  Prims.bool = (fun ( projectee  :  placement ) -> (match (projectee) with
| BuildTime -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Server : placement  ->  Prims.bool = (fun ( projectee  :  placement ) -> (match (projectee) with
| Server -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ClientDeclarative : placement  ->  Prims.bool = (fun ( projectee  :  placement ) -> (match (projectee) with
| ClientDeclarative -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ClientIsland : placement  ->  Prims.bool = (fun ( projectee  :  placement ) -> (match (projectee) with
| ClientIsland (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ClientIsland__item___0 : placement  ->  island_kind = (fun ( projectee  :  placement ) -> (match (projectee) with
| ClientIsland (_0) -> begin
     _0
     end))


let uu___is_Precomputed : placement  ->  Prims.bool = (fun ( projectee  :  placement ) -> (match (projectee) with
| Precomputed -> begin
     true
     end
| uu___ -> begin
     false
     end))

type capability = {c_id : Prims.string; c_signature : signature; c_determinism : determinism_source; c_placement : placement}


let __proj__Mkcapability__item__c_id : capability  ->  Prims.string = (fun ( projectee  :  capability ) -> (match (projectee) with
| {c_id = c_id; c_signature = c_signature; c_determinism = c_determinism; c_placement = c_placement} -> begin
     c_id
     end))


let __proj__Mkcapability__item__c_signature : capability  ->  signature = (fun ( projectee  :  capability ) -> (match (projectee) with
| {c_id = c_id; c_signature = c_signature; c_determinism = c_determinism; c_placement = c_placement} -> begin
     c_signature
     end))


let __proj__Mkcapability__item__c_determinism : capability  ->  determinism_source = (fun ( projectee  :  capability ) -> (match (projectee) with
| {c_id = c_id; c_signature = c_signature; c_determinism = c_determinism; c_placement = c_placement} -> begin
     c_determinism
     end))


let __proj__Mkcapability__item__c_placement : capability  ->  placement = (fun ( projectee  :  capability ) -> (match (projectee) with
| {c_id = c_id; c_signature = c_signature; c_determinism = c_determinism; c_placement = c_placement} -> begin
     c_placement
     end))


let create : Prims.string  ->  signature  ->  placement  ->  capability = (fun ( id  :  Prims.string ) ( sg  :  signature ) ( p  :  placement ) -> {c_id = id; c_signature = sg; c_determinism = sg.sg_effect.determinism; c_placement = p})


let determinism_tag_of : capability  ->  Prims.string = (fun ( c  :  capability ) -> (determinism_tag c.c_determinism))


type invocation = Prims.list<(Prims.string * Prims.string)>


let rec check_args : readers  ->  Prims.list<sig_entry>  ->  Prims.list<Prims.string>  ->  invocation  ->  outcome<unit, invoke_error> = (fun ( rd  :  readers ) ( holes  :  Prims.list<sig_entry> ) ( declared  :  Prims.list<Prims.string> ) ( a  :  invocation ) -> (match (a) with
| [] -> begin
     Ok (())
     end
| ((addr, value))::rest -> begin
     (match ((find_entry addr holes)) with
| FStar_Pervasives_Native.None -> begin
     Error (UnknownArg (addr, declared))
     end
| FStar_Pervasives_Native.Some (h) -> begin
     (match (h.s_space) with
| FStar_Pervasives_Native.None -> begin
     Error (UninvocableArg (addr))
     end
| FStar_Pervasives_Native.Some (space) -> begin
      
if (validate rd space value) then begin
     (check_args rd holes declared rest)
     end else begin
     Error (ArgOutOfSpace (addr, space, value))
     end
     end)
     end)
     end))


let rec unbound_required : Prims.list<sig_entry>  ->  invocation  ->  Prims.list<Prims.string> = (fun ( holes  :  Prims.list<sig_entry> ) ( a  :  invocation ) -> (match (holes) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (h.s_required && (not ((has_key h.s_addr a)))) then begin
     (h.s_addr)::(unbound_required t a)
     end else begin
     (unbound_required t a)
     end
     end))


let validate_args : readers  ->  capability  ->  invocation  ->  outcome<unit, invoke_error> = (fun ( rd  :  readers ) ( c  :  capability ) ( a  :  invocation ) -> (

let holes = c.c_signature.sg_holes
in (

let declared = (entry_addrs holes)
in (match ((check_args rd holes declared a)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((unbound_required holes a)) with
| [] -> begin
     Ok (())
     end
| u -> begin
     Error (RequiredArgsUnbound (u))
     end)
     end))))


let invoke = (fun ( rd  :  readers ) ( c  :  capability ) ( a  :  invocation ) ( body  :  unit  ->  outcome<'v, Prims.string> ) -> (match ((validate_args rd c a)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((body ())) with
| Ok (x) -> begin
     Ok (x)
     end
| Error (m) -> begin
     Error (BodyFailed (m))
     end)
     end))

type registry = {capabilities : Prims.list<capability>}


let __proj__Mkregistry__item__capabilities : registry  ->  Prims.list<capability> = (fun ( projectee  :  registry ) -> (match (projectee) with
| {capabilities = capabilities} -> begin
     capabilities
     end))


let empty : registry = {capabilities = []}


let rec find_cap : Prims.string  ->  Prims.list<capability>  ->  FStar_Pervasives_Native.option<capability> = (fun ( id  :  Prims.string ) ( cs  :  Prims.list<capability> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if (Prims.op_Equals c.c_id id) then begin
     FStar_Pervasives_Native.Some (c)
     end else begin
     (find_cap id t)
     end
     end))


let rec ids : Prims.list<capability>  ->  Prims.list<Prims.string> = (fun ( cs  :  Prims.list<capability> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (c.c_id)::(ids t)
     end))


let register : capability  ->  registry  ->  outcome<registry, invoke_error> = (fun ( c  :  capability ) ( r  :  registry ) -> (match ((find_cap c.c_id r.capabilities)) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     Error (DuplicateCapability (c.c_id))
     end
| FStar_Pervasives_Native.None -> begin
     Ok ({capabilities = (c)::r.capabilities})
     end))


let try_find_cap : Prims.string  ->  registry  ->  FStar_Pervasives_Native.option<capability> = (fun ( id  :  Prims.string ) ( r  :  registry ) -> (find_cap id r.capabilities))


let enumerate : registry  ->  Prims.list<capability> = (fun ( r  :  registry ) -> r.capabilities)


let dispatch = (fun ( rd  :  readers ) ( r  :  registry ) ( id  :  Prims.string ) ( a  :  invocation ) ( body  :  capability  ->  unit  ->  outcome<'v, Prims.string> ) -> (match ((find_cap id r.capabilities)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchCapability (id, (ids r.capabilities)))
     end
| FStar_Pervasives_Native.Some (c) -> begin
     (invoke rd c a (body c))
     end))


let rec all_declared : Prims.list<sig_entry>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( holes  :  Prims.list<sig_entry> ) ( ks  :  Prims.list<Prims.string> ) -> (match (ks) with
| [] -> begin
     true
     end
| (k)::t -> begin
     ((match ((find_entry k holes)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_declared holes t))
     end))


let hole_total : hole_decl  ->  Prims.bool = (fun ( h  :  hole_decl ) -> (match (h.h_kind) with
| RepeatHole (s) -> begin
     (is_bounded s)
     end
| uu___ -> begin
     true
     end))


let rename : (Prims.string  ->  Prims.string)  ->  hole_decl  ->  hole_decl = (fun ( f  :  Prims.string  ->  Prims.string ) ( h  :  hole_decl ) -> {h_addr = h.h_addr; h_name = (f h.h_name); h_kind = h.h_kind})


let renamed = (fun ( f  :  Prims.string  ->  Prims.string ) ( w  :  witness<'node> ) -> {holes = (fun ( m  :  'node ) -> (map (rename f) (w.holes m))); eff = w.eff; bind_hole = w.bind_hole; kind_tag = w.kind_tag; preorder = w.preorder})


let rec none_keyed = (fun ( a  :  args<'node> ) ( holes  :  Prims.list<hole_decl> ) -> (match (holes) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((not ((has_key h.h_addr a))) && (none_keyed a t))
     end))


let single_binding = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( k  :  Prims.string ) ( v  :  arg<'node> ) ( h  :  hole_decl ) ( cur  :  'node ) -> (match ((validate_arg rd w k h.h_kind v)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((w.bind_hole k v cur)) with
| Ok (cur') -> begin
     Ok (cur')
     end
| Error (m) -> begin
     Error (BindFailed (k, m))
     end)
     end))


let rec others : Prims.string  ->  Prims.list<hole_decl>  ->  Prims.list<hole_decl> = (fun ( k2  :  Prims.string ) ( holes  :  Prims.list<hole_decl> ) -> (match (holes) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (Prims.op_Less_Greater h.h_addr k2) then begin
     (h)::(others k2 t)
     end else begin
     (others k2 t)
     end
     end))


let rec all_covered : effect_class  ->  Prims.list<effect_class>  ->  Prims.bool = (fun ( c  :  effect_class ) ( l  :  Prims.list<effect_class> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((covers c x) && (all_covered c t))
     end))




