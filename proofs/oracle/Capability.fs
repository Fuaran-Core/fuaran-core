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

type determinism_source = {has_clock : Prims.bool; has_random : Prims.bool; has_network : Prims.bool}


let __proj__Mkdeterminism_source__item__has_clock : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| {has_clock = has_clock; has_random = has_random; has_network = has_network} -> begin
     has_clock
     end))


let __proj__Mkdeterminism_source__item__has_random : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| {has_clock = has_clock; has_random = has_random; has_network = has_network} -> begin
     has_random
     end))


let __proj__Mkdeterminism_source__item__has_network : determinism_source  ->  Prims.bool = (fun ( projectee  :  determinism_source ) -> (match (projectee) with
| {has_clock = has_clock; has_random = has_random; has_network = has_network} -> begin
     has_network
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


let deterministic : determinism_source = {has_clock = false; has_random = false; has_network = false}


let pure_deterministic : effect_class = {host = Pure; determinism = deterministic}


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


let max_int : Prims.int  ->  Prims.int  ->  Prims.int = (fun ( a  :  Prims.int ) ( b  :  Prims.int ) ->  
if (a >= b) then begin
     a
     end else begin
     b
     end)


let det_union : determinism_source  ->  determinism_source  ->  determinism_source = (fun ( a  :  determinism_source ) ( b  :  determinism_source ) -> {has_clock = (a.has_clock || b.has_clock); has_random = (a.has_random || b.has_random); has_network = (a.has_network || b.has_network)})


let det_subset : determinism_source  ->  determinism_source  ->  Prims.bool = (fun ( a  :  determinism_source ) ( b  :  determinism_source ) -> ((((not (a.has_clock)) || b.has_clock) && ((not (a.has_random)) || b.has_random)) && ((not (a.has_network)) || b.has_network)))


let join : effect_class  ->  effect_class  ->  effect_class = (fun ( a  :  effect_class ) ( b  :  effect_class ) -> {host = (host_of (max_int (host_rank a.host) (host_rank b.host))); determinism = (det_union a.determinism b.determinism)})


let covers : effect_class  ->  effect_class  ->  Prims.bool = (fun ( declared  :  effect_class ) ( actual  :  effect_class ) -> (((host_rank declared.host) >= (host_rank actual.host)) && (det_subset actual.determinism declared.determinism)))


let determinism_tag : determinism_source  ->  Prims.string = (fun ( d  :  determinism_source ) ->  
if d.has_clock then begin
      
if d.has_random then begin
      
if d.has_network then begin
     "clock+random+network"
     end else begin
     "clock+random"
     end
     end else begin
      
if d.has_network then begin
     "clock+network"
     end else begin
     "clock"
     end
     end
     end else begin
      
if d.has_random then begin
      
if d.has_network then begin
     "random+network"
     end else begin
     "random"
     end
     end else begin
      
if d.has_network then begin
     "network"
     end else begin
     "deterministic"
     end
     end
     end)


let det_of_tag : Prims.string  ->  FStar_Pervasives_Native.option<determinism_source> = (fun ( s  :  Prims.string ) ->  
if (Prims.op_Equals s "deterministic") then begin
     FStar_Pervasives_Native.Some (deterministic)
     end else begin
      
if (Prims.op_Equals s "clock") then begin
     FStar_Pervasives_Native.Some ({has_clock = true; has_random = false; has_network = false})
     end else begin
      
if (Prims.op_Equals s "random") then begin
     FStar_Pervasives_Native.Some ({has_clock = false; has_random = true; has_network = false})
     end else begin
      
if (Prims.op_Equals s "network") then begin
     FStar_Pervasives_Native.Some ({has_clock = false; has_random = false; has_network = true})
     end else begin
      
if (Prims.op_Equals s "clock+random") then begin
     FStar_Pervasives_Native.Some ({has_clock = true; has_random = true; has_network = false})
     end else begin
      
if (Prims.op_Equals s "clock+network") then begin
     FStar_Pervasives_Native.Some ({has_clock = true; has_random = false; has_network = true})
     end else begin
      
if (Prims.op_Equals s "random+network") then begin
     FStar_Pervasives_Native.Some ({has_clock = false; has_random = true; has_network = true})
     end else begin
      
if (Prims.op_Equals s "clock+random+network") then begin
     FStar_Pervasives_Native.Some ({has_clock = true; has_random = true; has_network = true})
     end else begin
     FStar_Pervasives_Native.None
     end
     end
     end
     end
     end
     end
     end
     end)

type value_space =
| IntRange of Prims.int * Prims.int
| FloatRange of Prims.string * Prims.string
| StringLen of Prims.int * Prims.int
| Enum of Prims.list<Prims.string>
| AnyString
| SlotTree of FStar_Pervasives_Native.option<Prims.string>


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


let uu___is_SlotTree : value_space  ->  Prims.bool = (fun ( projectee  :  value_space ) -> (match (projectee) with
| SlotTree (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SlotTree__item___0 : value_space  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( projectee  :  value_space ) -> (match (projectee) with
| SlotTree (_0) -> begin
     _0
     end))

type space_fault =
| SEmpty
| SNonFinite


let uu___is_SEmpty : space_fault  ->  Prims.bool = (fun ( projectee  :  space_fault ) -> (match (projectee) with
| SEmpty -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SNonFinite : space_fault  ->  Prims.bool = (fun ( projectee  :  space_fault ) -> (match (projectee) with
| SNonFinite -> begin
     true
     end
| uu___ -> begin
     false
     end))

type readers = {int_of : Prims.string  ->  FStar_Pervasives_Native.option<Prims.int>; float_in : Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.bool; str_len : Prims.string  ->  Prims.nat; kind_of : Prims.string  ->  FStar_Pervasives_Native.option<Prims.string>; float_fault : Prims.string  ->  Prims.string  ->  FStar_Pervasives_Native.option<space_fault>}


let __proj__Mkreaders__item__int_of : readers  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  readers ) -> (match (projectee) with
| {int_of = int_of; float_in = float_in; str_len = str_len; kind_of = kind_of; float_fault = float_fault} -> begin
     int_of
     end))


let __proj__Mkreaders__item__float_in : readers  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( projectee  :  readers ) -> (match (projectee) with
| {int_of = int_of; float_in = float_in; str_len = str_len; kind_of = kind_of; float_fault = float_fault} -> begin
     float_in
     end))


let __proj__Mkreaders__item__str_len : readers  ->  Prims.string  ->  Prims.nat = (fun ( projectee  :  readers ) -> (match (projectee) with
| {int_of = int_of; float_in = float_in; str_len = str_len; kind_of = kind_of; float_fault = float_fault} -> begin
     str_len
     end))


let __proj__Mkreaders__item__kind_of : readers  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( projectee  :  readers ) -> (match (projectee) with
| {int_of = int_of; float_in = float_in; str_len = str_len; kind_of = kind_of; float_fault = float_fault} -> begin
     kind_of
     end))


let __proj__Mkreaders__item__float_fault : readers  ->  Prims.string  ->  Prims.string  ->  FStar_Pervasives_Native.option<space_fault> = (fun ( projectee  :  readers ) -> (match (projectee) with
| {int_of = int_of; float_in = float_in; str_len = str_len; kind_of = kind_of; float_fault = float_fault} -> begin
     float_fault
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
     end
| SlotTree (c) -> begin
     (match ((rd.kind_of s)) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (k) -> begin
     (match (c) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| FStar_Pervasives_Native.Some (kc) -> begin
     (Prims.op_Equals k kc)
     end)
     end)
     end))


let max_repeat_count : Prims.int = (Prims.parse_int "1000000")


let is_count : value_space  ->  Prims.bool = (fun ( space  :  value_space ) -> (match (space) with
| IntRange (lo, hi) -> begin
     ((((Prims.parse_int "0") <= lo) && (lo <= hi)) && (hi <= max_repeat_count))
     end
| uu___ -> begin
     false
     end))


let space_wf : readers  ->  value_space  ->  FStar_Pervasives_Native.option<space_fault> = (fun ( rd  :  readers ) ( space  :  value_space ) -> (match (space) with
| IntRange (lo, hi) -> begin
      
if (lo > hi) then begin
     FStar_Pervasives_Native.Some (SEmpty)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| FloatRange (lo, hi) -> begin
     (rd.float_fault lo hi)
     end
| StringLen (lo, hi) -> begin
      
if ((lo > hi) || (hi < (Prims.parse_int "0"))) then begin
     FStar_Pervasives_Native.Some (SEmpty)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| Enum ([]) -> begin
     FStar_Pervasives_Native.Some (SEmpty)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
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

type decl_fault =
| EmptySpace of Prims.string * value_space
| NonFiniteBound of Prims.string
| DuplicateHoleAddr of Prims.string
| HoleUnderSlot of Prims.string


let uu___is_EmptySpace : decl_fault  ->  Prims.bool = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| EmptySpace (addr, space) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EmptySpace__item__addr : decl_fault  ->  Prims.string = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| EmptySpace (addr, space) -> begin
     addr
     end))


let __proj__EmptySpace__item__space : decl_fault  ->  value_space = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| EmptySpace (addr, space) -> begin
     space
     end))


let uu___is_NonFiniteBound : decl_fault  ->  Prims.bool = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| NonFiniteBound (addr) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NonFiniteBound__item__addr : decl_fault  ->  Prims.string = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| NonFiniteBound (addr) -> begin
     addr
     end))


let uu___is_DuplicateHoleAddr : decl_fault  ->  Prims.bool = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| DuplicateHoleAddr (addr) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateHoleAddr__item__addr : decl_fault  ->  Prims.string = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| DuplicateHoleAddr (addr) -> begin
     addr
     end))


let uu___is_HoleUnderSlot : decl_fault  ->  Prims.bool = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| HoleUnderSlot (node) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__HoleUnderSlot__item__node : decl_fault  ->  Prims.string = (fun ( projectee  :  decl_fault ) -> (match (projectee) with
| HoleUnderSlot (node) -> begin
     node
     end))

type apply_error =
| UnknownHoleAddr of Prims.string * Prims.list<Prims.string>
| ValueOutOfSpace of Prims.string * value_space * Prims.string
| RequiredHolesUnbound of Prims.list<Prims.string>
| NotASlot of Prims.string
| SlotKindMismatch of Prims.string * Prims.string * Prims.string
| NonTotal of Prims.string
| BindFailed of Prims.string * Prims.string
| SlotArgOpen of Prims.string * Prims.list<Prims.string>
| IllFormedResult of decl_fault


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


let uu___is_SlotArgOpen : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| SlotArgOpen (addr, holes) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SlotArgOpen__item__addr : apply_error  ->  Prims.string = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| SlotArgOpen (addr, holes) -> begin
     addr
     end))


let __proj__SlotArgOpen__item__holes : apply_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| SlotArgOpen (addr, holes) -> begin
     holes
     end))


let uu___is_IllFormedResult : apply_error  ->  Prims.bool = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| IllFormedResult (fault) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__IllFormedResult__item__fault : apply_error  ->  decl_fault = (fun ( projectee  :  apply_error ) -> (match (projectee) with
| IllFormedResult (fault) -> begin
     fault
     end))

type invoke_error =
| NoSuchCapability of Prims.string * Prims.list<Prims.string>
| DuplicateCapability of Prims.string
| UnknownArg of Prims.string * Prims.list<Prims.string>
| ArgOutOfSpace of Prims.string * value_space * Prims.string
| RequiredArgsUnbound of Prims.list<Prims.string>
| UninvocableArg of Prims.string
| BodyFailed of Prims.string
| NonTotalCapability of Prims.string * Prims.list<Prims.string>
| IllFormedCapability of Prims.string * decl_fault
| DuplicateArg of Prims.string


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


let uu___is_NonTotalCapability : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| NonTotalCapability (id, addrs) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NonTotalCapability__item__id : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| NonTotalCapability (id, addrs) -> begin
     id
     end))


let __proj__NonTotalCapability__item__addrs : invoke_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| NonTotalCapability (id, addrs) -> begin
     addrs
     end))


let uu___is_IllFormedCapability : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| IllFormedCapability (id, fault) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__IllFormedCapability__item__id : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| IllFormedCapability (id, fault) -> begin
     id
     end))


let __proj__IllFormedCapability__item__fault : invoke_error  ->  decl_fault = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| IllFormedCapability (id, fault) -> begin
     fault
     end))


let uu___is_DuplicateArg : invoke_error  ->  Prims.bool = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| DuplicateArg (addr) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateArg__item__addr : invoke_error  ->  Prims.string = (fun ( projectee  :  invoke_error ) -> (match (projectee) with
| DuplicateArg (addr) -> begin
     addr
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

type witness<'node> = {holes : 'node  ->  Prims.list<hole_decl>; eff : 'node  ->  effect_class; bind_hole : Prims.string  ->  arg<'node>  ->  'node  ->  outcome<'node, Prims.string>; kind_tag : 'node  ->  Prims.string; preorder : 'node  ->  Prims.list<'node>; children : 'node  ->  Prims.list<'node>; node_id : 'node  ->  Prims.string}


let __proj__Mkwitness__item__holes = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder; children = children; node_id = node_id} -> begin
     holes
     end))


let __proj__Mkwitness__item__eff = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder; children = children; node_id = node_id} -> begin
     eff
     end))


let __proj__Mkwitness__item__bind_hole = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder; children = children; node_id = node_id} -> begin
     bind_hole
     end))


let __proj__Mkwitness__item__kind_tag = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder; children = children; node_id = node_id} -> begin
     kind_tag
     end))


let __proj__Mkwitness__item__preorder = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder; children = children; node_id = node_id} -> begin
     preorder
     end))


let __proj__Mkwitness__item__children = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder; children = children; node_id = node_id} -> begin
     children
     end))


let __proj__Mkwitness__item__node_id = (fun ( projectee  :  witness<'node> ) -> (match (projectee) with
| {holes = holes; eff = eff; bind_hole = bind_hole; kind_tag = kind_tag; preorder = preorder; children = children; node_id = node_id} -> begin
     node_id
     end))


let entry_of : hole_decl  ->  sig_entry = (fun ( h  :  hole_decl ) -> (match (h.h_kind) with
| ValueHole (s) -> begin
     {s_addr = h.h_addr; s_name = h.h_name; s_kind = "value"; s_space = FStar_Pervasives_Native.Some (s); s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.None; s_required = true}
     end
| SlotHole (c) -> begin
     {s_addr = h.h_addr; s_name = h.h_name; s_kind = "slot"; s_space = FStar_Pervasives_Native.Some (SlotTree (c)); s_slot = c; s_action = FStar_Pervasives_Native.None; s_required = true}
     end
| RepeatHole (s) -> begin
     {s_addr = h.h_addr; s_name = h.h_name; s_kind = "repeat"; s_space = FStar_Pervasives_Native.Some (s); s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.None; s_required = (is_count s)}
     end
| ActionHole (e) -> begin
     {s_addr = h.h_addr; s_name = h.h_name; s_kind = "action"; s_space = FStar_Pervasives_Native.None; s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.Some (e); s_required = false}
     end))


let signature_of = (fun ( w  :  witness<'node> ) ( name  :  Prims.string ) ( n  :  'node ) -> {sg_name = name; sg_holes = (map entry_of (w.holes n)); sg_effect = (w.eff n)})


let signature_excluding : Prims.list<Prims.string>  ->  signature  ->  signature = (fun ( bound  :  Prims.list<Prims.string> ) ( sg  :  signature ) -> {sg_name = sg.sg_name; sg_holes = (excluding bound sg.sg_holes); sg_effect = sg.sg_effect})


let entry_total : sig_entry  ->  Prims.bool = (fun ( e  :  sig_entry ) -> (match (((e.s_kind), (e.s_space), (e.s_action))) with
| ("value", FStar_Pervasives_Native.Some (uu___), uu___1) -> begin
     true
     end
| ("slot", uu___, uu___1) -> begin
     true
     end
| ("repeat", FStar_Pervasives_Native.Some (s), uu___) -> begin
     (is_count s)
     end
| ("action", uu___, FStar_Pervasives_Native.Some (uu___1)) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let is_total : signature  ->  Prims.bool = (fun ( sg  :  signature ) -> (for_all entry_total sg.sg_holes))


let entry_space_fault : readers  ->  sig_entry  ->  FStar_Pervasives_Native.option<decl_fault> = (fun ( rd  :  readers ) ( e  :  sig_entry ) -> (match (e.s_space) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (sp) -> begin
     (match ((space_wf rd sp)) with
| FStar_Pervasives_Native.Some (SEmpty) -> begin
     FStar_Pervasives_Native.Some (EmptySpace (e.s_addr, sp))
     end
| FStar_Pervasives_Native.Some (SNonFinite) -> begin
     FStar_Pervasives_Native.Some (NonFiniteBound (e.s_addr))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end))


let rec validate_entries : readers  ->  Prims.list<Prims.string>  ->  Prims.list<sig_entry>  ->  FStar_Pervasives_Native.option<decl_fault> = (fun ( rd  :  readers ) ( seen  :  Prims.list<Prims.string> ) ( holes  :  Prims.list<sig_entry> ) -> (match (holes) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (e)::rest -> begin
      
if (mem e.s_addr seen) then begin
     FStar_Pervasives_Native.Some (DuplicateHoleAddr (e.s_addr))
     end else begin
     (match ((entry_space_fault rd e)) with
| FStar_Pervasives_Native.Some (f) -> begin
     FStar_Pervasives_Native.Some (f)
     end
| FStar_Pervasives_Native.None -> begin
     (validate_entries rd ((e.s_addr)::seen) rest)
     end)
     end
     end))


let validate_signature : readers  ->  signature  ->  FStar_Pervasives_Native.option<decl_fault> = (fun ( rd  :  readers ) ( sg  :  signature ) -> (validate_entries rd [] sg.sg_holes))


let rec slot_count : Prims.list<hole_decl>  ->  Prims.nat = (fun ( hs  :  Prims.list<hole_decl> ) -> (match (hs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (h)::t -> begin
     (( 
if (match (h.h_kind) with
| SlotHole (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "0")
     end) + (slot_count t))
     end))


let rec sum_slots = (fun ( w  :  witness<'node> ) ( cs  :  Prims.list<'node> ) -> (match (cs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (c)::t -> begin
     ((slot_count (w.holes c)) + (sum_slots w t))
     end))


let rec any_holes = (fun ( w  :  witness<'node> ) ( cs  :  Prims.list<'node> ) -> (match (cs) with
| [] -> begin
     false
     end
| (c)::t -> begin
     ((match ((w.holes c)) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) || (any_holes w t))
     end))


let slot_over_holes = (fun ( w  :  witness<'node> ) ( n  :  'node ) -> (

let cs = (w.children n)
in  
if ((any_holes w cs) && ((slot_count (w.holes n)) > (sum_slots w cs))) then begin
     FStar_Pervasives_Native.Some (HoleUnderSlot ((w.node_id n)))
     end else begin
     FStar_Pervasives_Native.None
     end))


let validate_decl = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( n  :  'node ) -> (match ((validate_signature rd (signature_of w "" n))) with
| FStar_Pervasives_Native.Some (f) -> begin
     FStar_Pervasives_Native.Some (f)
     end
| FStar_Pervasives_Native.None -> begin
     (try_pick (slot_over_holes w) (w.preorder n))
     end))


let non_total : hole_decl  ->  FStar_Pervasives_Native.option<apply_error> = (fun ( h  :  hole_decl ) -> (match (h.h_kind) with
| RepeatHole (s) -> begin
      
if (not ((is_count s))) then begin
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
      
if (not ((is_count space))) then begin
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


let open_slot = (fun ( w  :  witness<'node> ) ( b  :  (Prims.string * arg<'node>) ) -> (match (b) with
| (k, SlotArg (sub)) -> begin
     (match ((data_holes w sub)) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| hs -> begin
     FStar_Pervasives_Native.Some (SlotArgOpen (k, (map addr_of hs)))
     end)
     end
| (uu___, ValueArg (uu___1)) -> begin
     FStar_Pervasives_Native.None
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
     (match ( 
if strict then begin
     (try_pick (open_slot w) a)
     end else begin
     FStar_Pervasives_Native.None
     end) with
| FStar_Pervasives_Native.Some (e) -> begin
     Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     (bind_walk rd w strict a n [] holes)
     end)
     end))
     end)))


let apply = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( a  :  args<'node> ) ( n  :  'node ) -> (bind_args rd w true a n))


let curry = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( a  :  args<'node> ) ( n  :  'node ) -> (bind_args rd w false a n))


let composed_effect = (fun ( w  :  witness<'node> ) ( inner  :  'node ) ( outer  :  'node ) -> (join (w.eff outer) (w.eff inner)))


let wire = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( slot_addr  :  Prims.string ) ( inner  :  'node ) ( outer  :  'node ) -> (match ((w.bind_hole slot_addr (SlotArg (inner)) outer)) with
| Ok (n) -> begin
     (match ((validate_decl rd w n)) with
| FStar_Pervasives_Native.None -> begin
     Ok (n)
     end
| FStar_Pervasives_Native.Some (f) -> begin
     Error (IllFormedResult (f))
     end)
     end
| Error (m) -> begin
     Error (BindFailed (slot_addr, m))
     end))


let compose = (fun ( rd  :  readers ) ( w  :  witness<'node> ) ( slot_addr  :  Prims.string ) ( inner  :  'node ) ( outer  :  'node ) -> (

let holes = (w.holes outer)
in (match ((guard_total holes)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((guard_total (w.holes inner))) with
| FStar_Pervasives_Native.Some (e) -> begin
     Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((find_hole slot_addr holes)) with
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
     (wire rd w slot_addr inner outer)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (wire rd w slot_addr inner outer)
     end)
     end
| uu___ -> begin
     Error (NotASlot (slot_addr))
     end)
     end)
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


let arg_space : sig_entry  ->  FStar_Pervasives_Native.option<value_space> = (fun ( e  :  sig_entry ) -> (match (((e.s_kind), (e.s_space))) with
| ("slot", FStar_Pervasives_Native.None) -> begin
     FStar_Pervasives_Native.Some (SlotTree (e.s_slot))
     end
| (uu___, sp) -> begin
     sp
     end))


let rec repeated : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( seen  :  Prims.list<Prims.string> ) ( ks  :  Prims.list<Prims.string> ) -> (match (ks) with
| [] -> begin
     []
     end
| (k)::t -> begin
      
if (mem k seen) then begin
     (k)::(repeated seen t)
     end else begin
     (repeated ((k)::seen) t)
     end
     end))


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
     (match ((arg_space h)) with
| FStar_Pervasives_Native.None -> begin
     Error (UninvocableArg (addr))
     end
| FStar_Pervasives_Native.Some (space) -> begin
      
if ((match (space) with
| SlotTree (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (match ((rd.kind_of value)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     Error (UninvocableArg (addr))
     end else begin
      
if (validate rd space value) then begin
     (check_args rd holes declared rest)
     end else begin
     Error (ArgOutOfSpace (addr, space, value))
     end
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
in (match ((repeated [] (keys a))) with
| (d)::uu___ -> begin
     Error (DuplicateArg (d))
     end
| [] -> begin
     (match ((check_args rd holes declared a)) with
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
     end)
     end))))

type deferred<'a> =
| Pending
| Ready of 'a
| Failed of Prims.string


let uu___is_Pending = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Pending -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Ready = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Ready (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ready__item___0 = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Ready (_0) -> begin
     _0
     end))


let uu___is_Failed = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Failed (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Failed__item___0 = (fun ( projectee  :  deferred<'a> ) -> (match (projectee) with
| Failed (_0) -> begin
     _0
     end))


let invoke = (fun ( rd  :  readers ) ( c  :  capability ) ( a  :  invocation ) ( body  :  unit  ->  deferred<'v> ) -> (match ((validate_args rd c a)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((body ())) with
| Ready (x) -> begin
     Ok (Ready (x))
     end
| Pending -> begin
     Ok (Pending)
     end
| Failed (m) -> begin
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


let rec non_total_addrs : Prims.list<sig_entry>  ->  Prims.list<Prims.string> = (fun ( holes  :  Prims.list<sig_entry> ) -> (match (holes) with
| [] -> begin
     []
     end
| (e)::t -> begin
      
if (entry_total e) then begin
     (non_total_addrs t)
     end else begin
     (e.s_addr)::(non_total_addrs t)
     end
     end))


let register : readers  ->  capability  ->  registry  ->  outcome<registry, invoke_error> = (fun ( rd  :  readers ) ( c  :  capability ) ( r  :  registry ) -> (match ((find_cap c.c_id r.capabilities)) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     Error (DuplicateCapability (c.c_id))
     end
| FStar_Pervasives_Native.None -> begin
      
if (not ((is_total c.c_signature))) then begin
     Error (NonTotalCapability (c.c_id, (non_total_addrs c.c_signature.sg_holes)))
     end else begin
     (match ((validate_signature rd c.c_signature)) with
| FStar_Pervasives_Native.Some (f) -> begin
     Error (IllFormedCapability (c.c_id, f))
     end
| FStar_Pervasives_Native.None -> begin
     Ok ({capabilities = (c)::r.capabilities})
     end)
     end
     end))


let try_find_cap : Prims.string  ->  registry  ->  FStar_Pervasives_Native.option<capability> = (fun ( id  :  Prims.string ) ( r  :  registry ) -> (find_cap id r.capabilities))


let enumerate : registry  ->  Prims.list<capability> = (fun ( r  :  registry ) -> r.capabilities)


let dispatch = (fun ( rd  :  readers ) ( r  :  registry ) ( id  :  Prims.string ) ( a  :  invocation ) ( body  :  capability  ->  unit  ->  deferred<'v> ) -> (match ((find_cap id r.capabilities)) with
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


let rec args_in_space : readers  ->  Prims.list<sig_entry>  ->  invocation  ->  Prims.bool = (fun ( rd  :  readers ) ( holes  :  Prims.list<sig_entry> ) ( a  :  invocation ) -> (match (a) with
| [] -> begin
     true
     end
| ((addr, value))::rest -> begin
     ((match ((find_entry addr holes)) with
| FStar_Pervasives_Native.Some (h) -> begin
     (match ((arg_space h)) with
| FStar_Pervasives_Native.Some (sp) -> begin
     (validate rd sp value)
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)
     end
| FStar_Pervasives_Native.None -> begin
     false
     end) && (args_in_space rd holes rest))
     end))


let hole_total : hole_decl  ->  Prims.bool = (fun ( h  :  hole_decl ) -> (match (h.h_kind) with
| RepeatHole (s) -> begin
     (is_count s)
     end
| uu___ -> begin
     true
     end))


let rename : (Prims.string  ->  Prims.string)  ->  hole_decl  ->  hole_decl = (fun ( f  :  Prims.string  ->  Prims.string ) ( h  :  hole_decl ) -> {h_addr = h.h_addr; h_name = (f h.h_name); h_kind = h.h_kind})


let renamed = (fun ( f  :  Prims.string  ->  Prims.string ) ( w  :  witness<'node> ) -> {holes = (fun ( m  :  'node ) -> (map (rename f) (w.holes m))); eff = w.eff; bind_hole = w.bind_hole; kind_tag = w.kind_tag; preorder = w.preorder; children = w.children; node_id = w.node_id})


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

type key_renderers = {k_hash : Prims.string  ->  Prims.string; k_addr_le : Prims.string  ->  Prims.string  ->  Prims.bool; k_field : Prims.string  ->  Prims.string; k_canonical : value_space  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.string>}


let __proj__Mkkey_renderers__item__k_hash : key_renderers  ->  Prims.string  ->  Prims.string = (fun ( projectee  :  key_renderers ) -> (match (projectee) with
| {k_hash = k_hash; k_addr_le = k_addr_le; k_field = k_field; k_canonical = k_canonical} -> begin
     k_hash
     end))


let __proj__Mkkey_renderers__item__k_addr_le : key_renderers  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( projectee  :  key_renderers ) -> (match (projectee) with
| {k_hash = k_hash; k_addr_le = k_addr_le; k_field = k_field; k_canonical = k_canonical} -> begin
     k_addr_le
     end))


let __proj__Mkkey_renderers__item__k_field : key_renderers  ->  Prims.string  ->  Prims.string = (fun ( projectee  :  key_renderers ) -> (match (projectee) with
| {k_hash = k_hash; k_addr_le = k_addr_le; k_field = k_field; k_canonical = k_canonical} -> begin
     k_field
     end))


let __proj__Mkkey_renderers__item__k_canonical : key_renderers  ->  value_space  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( projectee  :  key_renderers ) -> (match (projectee) with
| {k_hash = k_hash; k_addr_le = k_addr_le; k_field = k_field; k_canonical = k_canonical} -> begin
     k_canonical
     end))


let rec insert_binding : key_renderers  ->  (Prims.string * Prims.string)  ->  invocation  ->  invocation = (fun ( kr  :  key_renderers ) ( x  :  (Prims.string * Prims.string) ) ( l  :  invocation ) -> (match (l) with
| [] -> begin
     (x)::[]
     end
| (y)::t -> begin
     (

let uu___ = x
in (match (uu___) with
| (xa, uu___1) -> begin
     (

let uu___2 = y
in (match (uu___2) with
| (ya, uu___3) -> begin
      
if (kr.k_addr_le xa ya) then begin
     (x)::l
     end else begin
     (y)::(insert_binding kr x t)
     end
     end))
     end))
     end))


let rec sort_bindings : key_renderers  ->  invocation  ->  invocation = (fun ( kr  :  key_renderers ) ( l  :  invocation ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (insert_binding kr x (sort_bindings kr t))
     end))


let rec binding_fields : invocation  ->  Prims.list<Prims.string> = (fun ( l  :  invocation ) -> (match (l) with
| [] -> begin
     []
     end
| ((a, v))::t -> begin
     (a)::(v)::(binding_fields t)
     end))


let rec key_fields : key_renderers  ->  Prims.list<Prims.string>  ->  Prims.string = (fun ( kr  :  key_renderers ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     ""
     end
| (x)::t -> begin
     (Prims.strcat (kr.k_field x) (key_fields kr t))
     end))


let key_canonical : key_renderers  ->  invocation  ->  Prims.string = (fun ( kr  :  key_renderers ) ( l  :  invocation ) -> (key_fields kr (binding_fields l)))


let spell : key_renderers  ->  capability  ->  (Prims.string * Prims.string)  ->  (Prims.string * Prims.string) = (fun ( kr  :  key_renderers ) ( c  :  capability ) ( b  :  (Prims.string * Prims.string) ) -> (

let uu___ = b
in (match (uu___) with
| (a, v) -> begin
     (match ((find_entry a c.c_signature.sg_holes)) with
| FStar_Pervasives_Native.Some (e) -> begin
     (match (e.s_space) with
| FStar_Pervasives_Native.Some (sp) -> begin
     (match ((kr.k_canonical sp v)) with
| FStar_Pervasives_Native.Some (v') -> begin
     ((a), (v'))
     end
| FStar_Pervasives_Native.None -> begin
     ((a), (v))
     end)
     end
| FStar_Pervasives_Native.None -> begin
     ((a), (v))
     end)
     end
| FStar_Pervasives_Native.None -> begin
     ((a), (v))
     end)
     end)))


let keyed : key_renderers  ->  capability  ->  invocation  ->  invocation = (fun ( kr  :  key_renderers ) ( c  :  capability ) ( a  :  invocation ) -> (map (spell kr c) a))


let invocation_key : key_renderers  ->  capability  ->  invocation  ->  Prims.string = (fun ( kr  :  key_renderers ) ( c  :  capability ) ( a  :  invocation ) -> (Prims.strcat c.c_id (Prims.strcat "#" (kr.k_hash (key_canonical kr (sort_bindings kr (keyed kr c a)))))))


let rec mem_binding : (Prims.string * Prims.string)  ->  invocation  ->  Prims.bool = (fun ( x  :  (Prims.string * Prims.string) ) ( l  :  invocation ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals x y) || (mem_binding x t))
     end))


let rec sorted_b : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  invocation  ->  Prims.bool = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( l  :  invocation ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::tl -> begin
     (match (tl) with
| [] -> begin
     true
     end
| (y)::uu___ -> begin
     ((le (FStar_Pervasives_Native.fst x) (FStar_Pervasives_Native.fst y)) && (sorted_b le tl))
     end)
     end))


let lift = (fun ( tree  :  Prims.string  ->  'node ) ( holes  :  Prims.list<sig_entry> ) ( b  :  (Prims.string * Prims.string) ) -> (

let uu___ = b
in (match (uu___) with
| (k, v) -> begin
     (match ((find_entry k holes)) with
| FStar_Pervasives_Native.Some (e) -> begin
      
if (Prims.op_Equals e.s_kind "slot") then begin
     ((k), (SlotArg ((tree v))))
     end else begin
     ((k), (ValueArg (v)))
     end
     end
| FStar_Pervasives_Native.None -> begin
     ((k), (ValueArg (v)))
     end)
     end)))


let lifted = (fun ( tree  :  Prims.string  ->  'node ) ( holes  :  Prims.list<sig_entry> ) ( a  :  invocation ) -> (map (lift tree holes) a))


let rec all_spaced : Prims.list<sig_entry>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( holes  :  Prims.list<sig_entry> ) ( ks  :  Prims.list<Prims.string> ) -> (match (ks) with
| [] -> begin
     true
     end
| (k)::t -> begin
     ((match ((find_entry k holes)) with
| FStar_Pervasives_Native.Some (e) -> begin
     (match ((arg_space e)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| FStar_Pervasives_Native.None -> begin
     false
     end) && (all_spaced holes t))
     end))


let rec subset : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( a  :  Prims.list<Prims.string> ) ( b  :  Prims.list<Prims.string> ) -> (match (a) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((mem h b) && (subset t b))
     end))

type handler_binding<'h> = {hb_handler : 'h; hb_effect : effect_class}


let __proj__Mkhandler_binding__item__hb_handler = (fun ( projectee  :  handler_binding<'h> ) -> (match (projectee) with
| {hb_handler = hb_handler; hb_effect = hb_effect} -> begin
     hb_handler
     end))


let __proj__Mkhandler_binding__item__hb_effect = (fun ( projectee  :  handler_binding<'h> ) -> (match (projectee) with
| {hb_handler = hb_handler; hb_effect = hb_effect} -> begin
     hb_effect
     end))


type handler_map<'h> = Prims.list<(Prims.string * handler_binding<'h>)>

type handler_table<'h> = {ht_handlers : handler_map<'h>}


let __proj__Mkhandler_table__item__ht_handlers = (fun ( projectee  :  handler_table<'h> ) -> (match (projectee) with
| {ht_handlers = ht_handlers} -> begin
     ht_handlers
     end))

type bind_handler_error =
| UnknownActionAddr of Prims.string * Prims.list<Prims.string>
| NotAnActionHole of Prims.string
| RequiredActionsUnbound of Prims.list<Prims.string>
| HandlerEffectExceedsCeiling of Prims.string * effect_class * effect_class


let uu___is_UnknownActionAddr : bind_handler_error  ->  Prims.bool = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| UnknownActionAddr (addr, declared_actions) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownActionAddr__item__addr : bind_handler_error  ->  Prims.string = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| UnknownActionAddr (addr, declared_actions) -> begin
     addr
     end))


let __proj__UnknownActionAddr__item__declared_actions : bind_handler_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| UnknownActionAddr (addr, declared_actions) -> begin
     declared_actions
     end))


let uu___is_NotAnActionHole : bind_handler_error  ->  Prims.bool = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| NotAnActionHole (addr) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NotAnActionHole__item__addr : bind_handler_error  ->  Prims.string = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| NotAnActionHole (addr) -> begin
     addr
     end))


let uu___is_RequiredActionsUnbound : bind_handler_error  ->  Prims.bool = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| RequiredActionsUnbound (addrs) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RequiredActionsUnbound__item__addrs : bind_handler_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| RequiredActionsUnbound (addrs) -> begin
     addrs
     end))


let uu___is_HandlerEffectExceedsCeiling : bind_handler_error  ->  Prims.bool = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| HandlerEffectExceedsCeiling (addr, ceiling, handler) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__HandlerEffectExceedsCeiling__item__addr : bind_handler_error  ->  Prims.string = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| HandlerEffectExceedsCeiling (addr, ceiling, handler) -> begin
     addr
     end))


let __proj__HandlerEffectExceedsCeiling__item__ceiling : bind_handler_error  ->  effect_class = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| HandlerEffectExceedsCeiling (addr, ceiling, handler) -> begin
     ceiling
     end))


let __proj__HandlerEffectExceedsCeiling__item__handler : bind_handler_error  ->  effect_class = (fun ( projectee  :  bind_handler_error ) -> (match (projectee) with
| HandlerEffectExceedsCeiling (addr, ceiling, handler) -> begin
     handler
     end))


let rec action_holes : Prims.list<hole_decl>  ->  Prims.list<(Prims.string * effect_class)> = (fun ( hs  :  Prims.list<hole_decl> ) -> (match (hs) with
| [] -> begin
     []
     end
| (h)::t -> begin
     (match (h.h_kind) with
| ActionHole (e) -> begin
     (((h.h_addr), (e)))::(action_holes t)
     end
| uu___ -> begin
     (action_holes t)
     end)
     end))


let rec check_keys = (fun ( action_addrs  :  Prims.list<Prims.string> ) ( all_addrs  :  Prims.list<Prims.string> ) ( hs  :  handler_map<'h> ) -> (match (hs) with
| [] -> begin
     Ok (())
     end
| ((addr, uu___))::rest -> begin
      
if (mem addr action_addrs) then begin
     (check_keys action_addrs all_addrs rest)
     end else begin
      
if (mem addr all_addrs) then begin
     Error (NotAnActionHole (addr))
     end else begin
     Error (UnknownActionAddr (addr, action_addrs))
     end
     end
     end))


let rec check_effects = (fun ( hs  :  handler_map<'h> ) ( holes  :  Prims.list<(Prims.string * effect_class)> ) -> (match (holes) with
| [] -> begin
     Ok (())
     end
| ((addr, ceiling))::rest -> begin
     (match ((assoc addr hs)) with
| FStar_Pervasives_Native.Some (hb) -> begin
      
if (not ((covers ceiling hb.hb_effect))) then begin
     Error (HandlerEffectExceedsCeiling (addr, ceiling, hb.hb_effect))
     end else begin
     (check_effects hs rest)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (check_effects hs rest)
     end)
     end))


let no_handler = (fun ( hs  :  handler_map<'h> ) ( a  :  Prims.string ) -> (not ((has_key a hs))))


let bind_handlers = (fun ( w  :  witness<'node> ) ( hs  :  handler_map<'h> ) ( n  :  'node ) -> (

let all_addrs = (map addr_of (w.holes n))
in (

let ah = (action_holes (w.holes n))
in (

let action_addrs = (keys ah)
in (match ((check_keys action_addrs all_addrs hs)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((check_effects hs ah)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((filter (no_handler hs) action_addrs)) with
| [] -> begin
     Ok ({ht_handlers = hs})
     end
| u -> begin
     Error (RequiredActionsUnbound (u))
     end)
     end)
     end)))))


let rec map_add = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( k  :  Prims.string ) ( v  :  'a ) ( l  :  Prims.list<(Prims.string * 'a)> ) -> (match (l) with
| [] -> begin
     (((k), (v)))::[]
     end
| ((k', v'))::t -> begin
      
if (Prims.op_Equals k k') then begin
     (((k), (v)))::t
     end else begin
      
if (le k k') then begin
     (((k), (v)))::l
     end else begin
     (((k'), (v')))::(map_add le k v t)
     end
     end
     end))


let rec map_fold = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( acc  :  Prims.list<(Prims.string * 'a)> ) ( l  :  Prims.list<(Prims.string * 'a)> ) -> (match (l) with
| [] -> begin
     acc
     end
| ((k, v))::t -> begin
     (map_fold le (map_add le k v acc) t)
     end))


let map_of_list = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( l  :  Prims.list<(Prims.string * 'a)> ) -> (map_fold le [] l))


let bind_handlers_of = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( w  :  witness<'node> ) ( arriving  :  handler_map<'h> ) ( n  :  'node ) -> (bind_handlers w (map_of_list le arriving) n))


let rec sorted_k = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( l  :  Prims.list<(Prims.string * 'a)> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::tl -> begin
     (match (tl) with
| [] -> begin
     true
     end
| (y)::uu___ -> begin
     ((le (FStar_Pervasives_Native.fst x) (FStar_Pervasives_Native.fst y)) && (sorted_k le tl))
     end)
     end))


let exceeds = (fun ( hs  :  handler_map<'h> ) ( hole  :  (Prims.string * effect_class) ) -> (match ((assoc (FStar_Pervasives_Native.fst hole) hs)) with
| FStar_Pervasives_Native.Some (hb) -> begin
     (not ((covers (FStar_Pervasives_Native.snd hole) hb.hb_effect)))
     end
| FStar_Pervasives_Native.None -> begin
     false
     end))


let bound_within = (fun ( hs  :  handler_map<'h> ) ( hole  :  (Prims.string * effect_class) ) -> (match ((assoc (FStar_Pervasives_Native.fst hole) hs)) with
| FStar_Pervasives_Native.Some (hb) -> begin
     (covers (FStar_Pervasives_Native.snd hole) hb.hb_effect)
     end
| FStar_Pervasives_Native.None -> begin
     false
     end))


let rec diff : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( a  :  Prims.list<Prims.string> ) ( b  :  Prims.list<Prims.string> ) -> (match (a) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (mem h b) then begin
     (diff t b)
     end else begin
     (h)::(diff t b)
     end
     end))


let union : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( a  :  Prims.list<Prims.string> ) ( b  :  Prims.list<Prims.string> ) -> (app a (diff b a)))


let rec dedup : Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (mem h t) then begin
     (dedup t)
     end else begin
     (h)::(dedup t)
     end
     end))

type arg_source =
| Literal of Prims.string
| FromNode of Prims.string


let uu___is_Literal : arg_source  ->  Prims.bool = (fun ( projectee  :  arg_source ) -> (match (projectee) with
| Literal (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Literal__item__value : arg_source  ->  Prims.string = (fun ( projectee  :  arg_source ) -> (match (projectee) with
| Literal (value) -> begin
     value
     end))


let uu___is_FromNode : arg_source  ->  Prims.bool = (fun ( projectee  :  arg_source ) -> (match (projectee) with
| FromNode (node_id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__FromNode__item__node_id : arg_source  ->  Prims.string = (fun ( projectee  :  arg_source ) -> (match (projectee) with
| FromNode (node_id) -> begin
     node_id
     end))

type pipeline_node =
| Source of Prims.string * Prims.string * value_space
| Invoke of Prims.string * Prims.string * value_space * Prims.list<(Prims.string * arg_source)>


let uu___is_Source : pipeline_node  ->  Prims.bool = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Source (id, data_ref, output_type) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Source__item__id : pipeline_node  ->  Prims.string = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Source (id, data_ref, output_type) -> begin
     id
     end))


let __proj__Source__item__data_ref : pipeline_node  ->  Prims.string = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Source (id, data_ref, output_type) -> begin
     data_ref
     end))


let __proj__Source__item__output_type : pipeline_node  ->  value_space = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Source (id, data_ref, output_type) -> begin
     output_type
     end))


let uu___is_Invoke : pipeline_node  ->  Prims.bool = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Invoke (id, capability_id, output_type, args1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Invoke__item__id : pipeline_node  ->  Prims.string = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Invoke (id, capability_id, output_type, args1) -> begin
     id
     end))


let __proj__Invoke__item__capability_id : pipeline_node  ->  Prims.string = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Invoke (id, capability_id, output_type, args1) -> begin
     capability_id
     end))


let __proj__Invoke__item__output_type : pipeline_node  ->  value_space = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Invoke (id, capability_id, output_type, args1) -> begin
     output_type
     end))


let __proj__Invoke__item__args : pipeline_node  ->  Prims.list<(Prims.string * arg_source)> = (fun ( projectee  :  pipeline_node ) -> (match (projectee) with
| Invoke (id, capability_id, output_type, args1) -> begin
     args1
     end))

type pipeline = {p_nodes : Prims.list<pipeline_node>}


let __proj__Mkpipeline__item__p_nodes : pipeline  ->  Prims.list<pipeline_node> = (fun ( projectee  :  pipeline ) -> (match (projectee) with
| {p_nodes = p_nodes} -> begin
     p_nodes
     end))

type pipeline_error =
| DuplicateNode of Prims.string
| UnknownNode of Prims.string
| PipelineNoSuchCapability of Prims.string * Prims.list<Prims.string>
| PipelineArgRefused of Prims.string * invoke_error
| PipelineCycle of Prims.string * Prims.list<Prims.string>
| EdgeTypeMismatch of Prims.string * Prims.string * Prims.string * Prims.string
| PipelineForwardEdge of Prims.string * Prims.string * Prims.string


let uu___is_DuplicateNode : pipeline_error  ->  Prims.bool = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| DuplicateNode (id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateNode__item__id : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| DuplicateNode (id) -> begin
     id
     end))


let uu___is_UnknownNode : pipeline_error  ->  Prims.bool = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| UnknownNode (id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownNode__item__id : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| UnknownNode (id) -> begin
     id
     end))


let uu___is_PipelineNoSuchCapability : pipeline_error  ->  Prims.bool = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineNoSuchCapability (id, known) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PipelineNoSuchCapability__item__id : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineNoSuchCapability (id, known) -> begin
     id
     end))


let __proj__PipelineNoSuchCapability__item__known : pipeline_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineNoSuchCapability (id, known) -> begin
     known
     end))


let uu___is_PipelineArgRefused : pipeline_error  ->  Prims.bool = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineArgRefused (at, reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PipelineArgRefused__item__at : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineArgRefused (at, reason) -> begin
     at
     end))


let __proj__PipelineArgRefused__item__reason : pipeline_error  ->  invoke_error = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineArgRefused (at, reason) -> begin
     reason
     end))


let uu___is_PipelineCycle : pipeline_error  ->  Prims.bool = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineCycle (at, cycle) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PipelineCycle__item__at : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineCycle (at, cycle) -> begin
     at
     end))


let __proj__PipelineCycle__item__cycle : pipeline_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineCycle (at, cycle) -> begin
     cycle
     end))


let uu___is_EdgeTypeMismatch : pipeline_error  ->  Prims.bool = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| EdgeTypeMismatch (at, addr, producer, consumer) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EdgeTypeMismatch__item__at : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| EdgeTypeMismatch (at, addr, producer, consumer) -> begin
     at
     end))


let __proj__EdgeTypeMismatch__item__addr : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| EdgeTypeMismatch (at, addr, producer, consumer) -> begin
     addr
     end))


let __proj__EdgeTypeMismatch__item__producer : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| EdgeTypeMismatch (at, addr, producer, consumer) -> begin
     producer
     end))


let __proj__EdgeTypeMismatch__item__consumer : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| EdgeTypeMismatch (at, addr, producer, consumer) -> begin
     consumer
     end))


let uu___is_PipelineForwardEdge : pipeline_error  ->  Prims.bool = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineForwardEdge (at, addr, upstream) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PipelineForwardEdge__item__at : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineForwardEdge (at, addr, upstream) -> begin
     at
     end))


let __proj__PipelineForwardEdge__item__addr : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineForwardEdge (at, addr, upstream) -> begin
     addr
     end))


let __proj__PipelineForwardEdge__item__upstream : pipeline_error  ->  Prims.string = (fun ( projectee  :  pipeline_error ) -> (match (projectee) with
| PipelineForwardEdge (at, addr, upstream) -> begin
     upstream
     end))

type pipeline_arg<'v> =
| FromUpstream of 'v
| LiteralArg of Prims.string


let uu___is_FromUpstream = (fun ( projectee  :  pipeline_arg<'v> ) -> (match (projectee) with
| FromUpstream (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__FromUpstream__item___0 = (fun ( projectee  :  pipeline_arg<'v> ) -> (match (projectee) with
| FromUpstream (_0) -> begin
     _0
     end))


let uu___is_LiteralArg = (fun ( projectee  :  pipeline_arg<'v> ) -> (match (projectee) with
| LiteralArg (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LiteralArg__item___0 = (fun ( projectee  :  pipeline_arg<'v> ) -> (match (projectee) with
| LiteralArg (_0) -> begin
     _0
     end))

type pipeline_eval_error =
| EvalIllTyped of pipeline_error
| EvalNodeFailed of Prims.string * Prims.string
| EvalArgRefused of Prims.string * invoke_error


let uu___is_EvalIllTyped : pipeline_eval_error  ->  Prims.bool = (fun ( projectee  :  pipeline_eval_error ) -> (match (projectee) with
| EvalIllTyped (reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EvalIllTyped__item__reason : pipeline_eval_error  ->  pipeline_error = (fun ( projectee  :  pipeline_eval_error ) -> (match (projectee) with
| EvalIllTyped (reason) -> begin
     reason
     end))


let uu___is_EvalNodeFailed : pipeline_eval_error  ->  Prims.bool = (fun ( projectee  :  pipeline_eval_error ) -> (match (projectee) with
| EvalNodeFailed (at, message) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EvalNodeFailed__item__at : pipeline_eval_error  ->  Prims.string = (fun ( projectee  :  pipeline_eval_error ) -> (match (projectee) with
| EvalNodeFailed (at, message) -> begin
     at
     end))


let __proj__EvalNodeFailed__item__message : pipeline_eval_error  ->  Prims.string = (fun ( projectee  :  pipeline_eval_error ) -> (match (projectee) with
| EvalNodeFailed (at, message) -> begin
     message
     end))


let uu___is_EvalArgRefused : pipeline_eval_error  ->  Prims.bool = (fun ( projectee  :  pipeline_eval_error ) -> (match (projectee) with
| EvalArgRefused (at, reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EvalArgRefused__item__at : pipeline_eval_error  ->  Prims.string = (fun ( projectee  :  pipeline_eval_error ) -> (match (projectee) with
| EvalArgRefused (at, reason) -> begin
     at
     end))


let __proj__EvalArgRefused__item__reason : pipeline_eval_error  ->  invoke_error = (fun ( projectee  :  pipeline_eval_error ) -> (match (projectee) with
| EvalArgRefused (at, reason) -> begin
     reason
     end))

type capability_lookup = {lk_find : Prims.string  ->  FStar_Pervasives_Native.option<capability>; lk_known : Prims.list<Prims.string>}


let __proj__Mkcapability_lookup__item__lk_find : capability_lookup  ->  Prims.string  ->  FStar_Pervasives_Native.option<capability> = (fun ( projectee  :  capability_lookup ) -> (match (projectee) with
| {lk_find = lk_find; lk_known = lk_known} -> begin
     lk_find
     end))


let __proj__Mkcapability_lookup__item__lk_known : capability_lookup  ->  Prims.list<Prims.string> = (fun ( projectee  :  capability_lookup ) -> (match (projectee) with
| {lk_find = lk_find; lk_known = lk_known} -> begin
     lk_known
     end))


let lookup_of_registry : registry  ->  capability_lookup = (fun ( r  :  registry ) -> {lk_find = (fun ( id  :  Prims.string ) -> (find_cap id r.capabilities)); lk_known = (ids r.capabilities)})

type feed_readers = {float_within : Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.bool; int_within : Prims.string  ->  Prims.string  ->  Prims.int  ->  Prims.int  ->  Prims.bool}


let __proj__Mkfeed_readers__item__float_within : feed_readers  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( projectee  :  feed_readers ) -> (match (projectee) with
| {float_within = float_within; int_within = int_within} -> begin
     float_within
     end))


let __proj__Mkfeed_readers__item__int_within : feed_readers  ->  Prims.string  ->  Prims.string  ->  Prims.int  ->  Prims.int  ->  Prims.bool = (fun ( projectee  :  feed_readers ) -> (match (projectee) with
| {float_within = float_within; int_within = int_within} -> begin
     int_within
     end))


let rec all_len_in : readers  ->  Prims.int  ->  Prims.int  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( rd  :  readers ) ( lo  :  Prims.int ) ( hi  :  Prims.int ) ( xs  :  Prims.list<Prims.string> ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((((rd.str_len x) >= lo) && ((rd.str_len x) <= hi)) && (all_len_in rd lo hi t))
     end))


let subsumes : feed_readers  ->  readers  ->  value_space  ->  value_space  ->  Prims.bool = (fun ( fr  :  feed_readers ) ( rd  :  readers ) ( required  :  value_space ) ( available  :  value_space ) -> (match (((required), (available))) with
| (IntRange (rl, rh), IntRange (al, ah)) -> begin
     ((rl <= al) && (ah <= rh))
     end
| (FloatRange (rl, rh), FloatRange (al, ah)) -> begin
     (fr.float_within rl rh al ah)
     end
| (FloatRange (rl, rh), IntRange (al, ah)) -> begin
     (fr.int_within rl rh al ah)
     end
| (StringLen (rl, rh), StringLen (al, ah)) -> begin
     ((rl <= al) && (ah <= rh))
     end
| (StringLen (rl, rh), Enum (xs)) -> begin
     (all_len_in rd rl rh xs)
     end
| (Enum (rs), Enum (xs)) -> begin
     (subset xs rs)
     end
| (AnyString, a) -> begin
     (not ((match (a) with
| SlotTree (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end)))
     end
| (SlotTree (FStar_Pervasives_Native.None), SlotTree (uu___)) -> begin
     true
     end
| (SlotTree (FStar_Pervasives_Native.Some (rk)), SlotTree (FStar_Pervasives_Native.Some (ak))) -> begin
     (Prims.op_Equals rk ak)
     end
| uu___ -> begin
     false
     end))


let space_tag : value_space  ->  Prims.string = (fun ( s  :  value_space ) -> (match (s) with
| IntRange (uu___, uu___1) -> begin
     "int"
     end
| FloatRange (uu___, uu___1) -> begin
     "float"
     end
| StringLen (uu___, uu___1) -> begin
     "string"
     end
| Enum (uu___) -> begin
     "enum"
     end
| AnyString -> begin
     "anyString"
     end
| SlotTree (uu___) -> begin
     "slotTree"
     end))


let node_id : pipeline_node  ->  Prims.string = (fun ( n  :  pipeline_node ) -> (match (n) with
| Source (id, uu___, uu___1) -> begin
     id
     end
| Invoke (id, uu___, uu___1, uu___2) -> begin
     id
     end))


let node_output : pipeline_node  ->  value_space = (fun ( n  :  pipeline_node ) -> (match (n) with
| Source (uu___, uu___1, ty) -> begin
     ty
     end
| Invoke (uu___, uu___1, ty, uu___2) -> begin
     ty
     end))


let rec upstreams_of : Prims.list<(Prims.string * arg_source)>  ->  Prims.list<Prims.string> = (fun ( a  :  Prims.list<(Prims.string * arg_source)> ) -> (match (a) with
| [] -> begin
     []
     end
| ((uu___, FromNode (up)))::t -> begin
     (up)::(upstreams_of t)
     end
| ((uu___, Literal (uu___1)))::t -> begin
     (upstreams_of t)
     end))


let upstreams : pipeline_node  ->  Prims.list<Prims.string> = (fun ( n  :  pipeline_node ) -> (match (n) with
| Source (uu___, uu___1, uu___2) -> begin
     []
     end
| Invoke (uu___, uu___1, uu___2, a) -> begin
     (upstreams_of a)
     end))


let rec node_ids : Prims.list<pipeline_node>  ->  Prims.list<Prims.string> = (fun ( ns  :  Prims.list<pipeline_node> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     ((node_id n))::(node_ids t)
     end))


let rec find_node : Prims.string  ->  Prims.list<pipeline_node>  ->  FStar_Pervasives_Native.option<pipeline_node> = (fun ( id  :  Prims.string ) ( ns  :  Prims.list<pipeline_node> ) -> (match (ns) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (n)::t -> begin
      
if (Prims.op_Equals (node_id n) id) then begin
     FStar_Pervasives_Native.Some (n)
     end else begin
     (find_node id t)
     end
     end))


let rec first_dup : Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::t -> begin
      
if (mem x t) then begin
     FStar_Pervasives_Native.Some (x)
     end else begin
     (first_dup t)
     end
     end))


let rec index_of : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.nat = (fun ( k  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (x)::t -> begin
      
if (Prims.op_Equals x k) then begin
     (Prims.parse_int "0")
     end else begin
     ((Prims.parse_int "1") + (index_of k t))
     end
     end))


let rec walk : Prims.list<pipeline_node>  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.string  ->  (Prims.list<Prims.string> * FStar_Pervasives_Native.option<Prims.list<Prims.string>>) = (fun ( ns  :  Prims.list<pipeline_node> ) ( target  :  Prims.string ) ( seen  :  Prims.list<Prims.string> ) ( cur  :  Prims.string ) ->  
if (mem cur seen) then begin
     ((seen), (FStar_Pervasives_Native.None))
     end else begin
     (

let seen1 = (cur)::seen
in (match ((find_node cur ns)) with
| FStar_Pervasives_Native.None -> begin
     ((seen1), (FStar_Pervasives_Native.None))
     end
| FStar_Pervasives_Native.Some (n) -> begin
     (

let ups = (upstreams n)
in  
if (mem target ups) then begin
     ((seen1), (FStar_Pervasives_Native.Some ((cur)::[])))
     end else begin
     (try_ups ns target cur seen1 ups)
     end)
     end))
     end)
and try_ups : Prims.list<pipeline_node>  ->  Prims.string  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  (Prims.list<Prims.string> * FStar_Pervasives_Native.option<Prims.list<Prims.string>>) = (fun ( ns  :  Prims.list<pipeline_node> ) ( target  :  Prims.string ) ( cur  :  Prims.string ) ( seen  :  Prims.list<Prims.string> ) ( ups  :  Prims.list<Prims.string> ) -> (match (ups) with
| [] -> begin
     ((seen), (FStar_Pervasives_Native.None))
     end
| (u)::rest -> begin
     (

let r = (walk ns target seen u)
in (match ((FStar_Pervasives_Native.snd r)) with
| FStar_Pervasives_Native.Some (path) -> begin
     (((FStar_Pervasives_Native.fst r)), (FStar_Pervasives_Native.Some ((cur)::path)))
     end
| FStar_Pervasives_Native.None -> begin
     (try_ups ns target cur (FStar_Pervasives_Native.fst r) rest)
     end))
     end))


let path_to : Prims.list<pipeline_node>  ->  Prims.string  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>> = (fun ( ns  :  Prims.list<pipeline_node> ) ( target  :  Prims.string ) ( start  :  Prims.string ) -> (FStar_Pervasives_Native.snd (walk ns target [] start)))


let edge_fault : feed_readers  ->  readers  ->  Prims.list<pipeline_node>  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  value_space  ->  FStar_Pervasives_Native.option<pipeline_error> = (fun ( fr  :  feed_readers ) ( rd  :  readers ) ( ns  :  Prims.list<pipeline_node> ) ( all_ids  :  Prims.list<Prims.string> ) ( nid  :  Prims.string ) ( addr  :  Prims.string ) ( up  :  Prims.string ) ( arg_sp  :  value_space ) -> (match ((find_node up ns)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (UnknownNode (up))
     end
| FStar_Pervasives_Native.Some (up_node) -> begin
      
if (Prims.op_Equals up nid) then begin
     FStar_Pervasives_Native.Some (PipelineCycle (nid, (nid)::[]))
     end else begin
      
if ((index_of up all_ids) > (index_of nid all_ids)) then begin
     (match ((path_to ns nid up)) with
| FStar_Pervasives_Native.Some (path) -> begin
     FStar_Pervasives_Native.Some (PipelineCycle (nid, (nid)::path))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (PipelineForwardEdge (nid, addr, up))
     end)
     end else begin
      
if (subsumes fr rd arg_sp (node_output up_node)) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (EdgeTypeMismatch (nid, addr, (space_tag (node_output up_node)), (space_tag arg_sp)))
     end
     end
     end
     end))


let arg_fault : readers  ->  capability  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.string  ->  FStar_Pervasives_Native.option<invoke_error> = (fun ( rd  :  readers ) ( c  :  capability ) ( declared  :  Prims.list<Prims.string> ) ( addr  :  Prims.string ) ( value  :  Prims.string ) -> (match ((find_entry addr c.c_signature.sg_holes)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (UnknownArg (addr, declared))
     end
| FStar_Pervasives_Native.Some (h) -> begin
     (match ((arg_space h)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (UninvocableArg (addr))
     end
| FStar_Pervasives_Native.Some (space) -> begin
      
if ((match (space) with
| SlotTree (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (match ((rd.kind_of value)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     FStar_Pervasives_Native.Some (UninvocableArg (addr))
     end else begin
      
if (validate rd space value) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some (ArgOutOfSpace (addr, space, value))
     end
     end
     end)
     end))


let pipe_arg_fault : feed_readers  ->  readers  ->  Prims.list<pipeline_node>  ->  Prims.list<Prims.string>  ->  capability  ->  Prims.list<Prims.string>  ->  Prims.string  ->  (Prims.string * arg_source)  ->  FStar_Pervasives_Native.option<pipeline_error> = (fun ( fr  :  feed_readers ) ( rd  :  readers ) ( ns  :  Prims.list<pipeline_node> ) ( all_ids  :  Prims.list<Prims.string> ) ( cap  :  capability ) ( declared  :  Prims.list<Prims.string> ) ( nid  :  Prims.string ) ( b  :  (Prims.string * arg_source) ) -> (

let uu___ = b
in (match (uu___) with
| (addr, src) -> begin
     (match (src) with
| Literal (v) -> begin
     (match ((arg_fault rd cap declared addr v)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (PipelineArgRefused (nid, e))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| FromNode (up) -> begin
     (match ((find_entry addr cap.c_signature.sg_holes)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (PipelineArgRefused (nid, UnknownArg (addr, declared)))
     end
| FStar_Pervasives_Native.Some (h) -> begin
     (match (h.s_space) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (PipelineArgRefused (nid, UninvocableArg (addr)))
     end
| FStar_Pervasives_Native.Some (sp) -> begin
     (edge_fault fr rd ns all_ids nid addr up sp)
     end)
     end)
     end)
     end)))


let rec unbound_keys : Prims.list<sig_entry>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( holes  :  Prims.list<sig_entry> ) ( ks  :  Prims.list<Prims.string> ) -> (match (holes) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (h.s_required && (not ((mem h.s_addr ks)))) then begin
     (h.s_addr)::(unbound_keys t ks)
     end else begin
     (unbound_keys t ks)
     end
     end))


let node_fault : feed_readers  ->  readers  ->  capability_lookup  ->  Prims.list<pipeline_node>  ->  Prims.list<Prims.string>  ->  pipeline_node  ->  FStar_Pervasives_Native.option<pipeline_error> = (fun ( fr  :  feed_readers ) ( rd  :  readers ) ( lk  :  capability_lookup ) ( ns  :  Prims.list<pipeline_node> ) ( all_ids  :  Prims.list<Prims.string> ) ( n  :  pipeline_node ) -> (match (n) with
| Source (uu___, uu___1, uu___2) -> begin
     FStar_Pervasives_Native.None
     end
| Invoke (nid, cap_id, uu___, a) -> begin
     (match ((lk.lk_find cap_id)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (PipelineNoSuchCapability (cap_id, lk.lk_known))
     end
| FStar_Pervasives_Native.Some (cap) -> begin
     (

let holes = cap.c_signature.sg_holes
in (

let declared = (entry_addrs holes)
in (match ((repeated [] (keys a))) with
| (d)::uu___1 -> begin
     FStar_Pervasives_Native.Some (PipelineArgRefused (nid, DuplicateArg (d)))
     end
| [] -> begin
     (match ((try_pick (pipe_arg_fault fr rd ns all_ids cap declared nid) a)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (e)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((unbound_keys holes (keys a))) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| u -> begin
     FStar_Pervasives_Native.Some (PipelineArgRefused (nid, RequiredArgsUnbound (u)))
     end)
     end)
     end)))
     end)
     end))


let rec go_check : feed_readers  ->  readers  ->  capability_lookup  ->  Prims.list<pipeline_node>  ->  Prims.list<Prims.string>  ->  Prims.list<pipeline_node>  ->  outcome<unit, pipeline_error> = (fun ( fr  :  feed_readers ) ( rd  :  readers ) ( lk  :  capability_lookup ) ( ns  :  Prims.list<pipeline_node> ) ( all_ids  :  Prims.list<Prims.string> ) ( rest  :  Prims.list<pipeline_node> ) -> (match (rest) with
| [] -> begin
     Ok (())
     end
| (n)::t -> begin
     (match ((node_fault fr rd lk ns all_ids n)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     (go_check fr rd lk ns all_ids t)
     end)
     end))


let type_check : feed_readers  ->  readers  ->  capability_lookup  ->  pipeline  ->  outcome<unit, pipeline_error> = (fun ( fr  :  feed_readers ) ( rd  :  readers ) ( lk  :  capability_lookup ) ( p  :  pipeline ) -> (

let all_ids = (node_ids p.p_nodes)
in (match ((first_dup all_ids)) with
| FStar_Pervasives_Native.Some (d) -> begin
     Error (DuplicateNode (d))
     end
| FStar_Pervasives_Native.None -> begin
     (go_check fr rd lk p.p_nodes all_ids p.p_nodes)
     end)))


type node_body<'v> = pipeline_node  ->  Prims.list<(Prims.string * pipeline_arg<'v>)>  ->  outcome<'v, Prims.string>


let rec space_of : Prims.string  ->  Prims.list<sig_entry>  ->  FStar_Pervasives_Native.option<value_space> = (fun ( addr  :  Prims.string ) ( holes  :  Prims.list<sig_entry> ) -> (match (holes) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (h)::t -> begin
      
if (Prims.op_Equals h.s_addr addr) then begin
     (match (h.s_space) with
| FStar_Pervasives_Native.Some (s) -> begin
     FStar_Pervasives_Native.Some (s)
     end
| FStar_Pervasives_Native.None -> begin
     (space_of addr t)
     end)
     end else begin
     (space_of addr t)
     end
     end))


let arg_space_of : capability_lookup  ->  Prims.string  ->  Prims.string  ->  FStar_Pervasives_Native.option<value_space> = (fun ( lk  :  capability_lookup ) ( cap_id  :  Prims.string ) ( addr  :  Prims.string ) -> (match ((lk.lk_find cap_id)) with
| FStar_Pervasives_Native.Some (c) -> begin
     (space_of addr c.c_signature.sg_holes)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end))


let rec resolve_args = (fun ( rd  :  readers ) ( lk  :  capability_lookup ) ( spell1  :  'v  ->  Prims.string ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( nid  :  Prims.string ) ( cap_id  :  Prims.string ) ( a  :  Prims.list<(Prims.string * arg_source)> ) ( acc  :  Prims.list<(Prims.string * pipeline_arg<'v>)> ) -> (match (a) with
| [] -> begin
     Ok (acc)
     end
| ((addr, src))::rest -> begin
     (match (src) with
| Literal (s) -> begin
     (resolve_args rd lk spell1 results nid cap_id rest ((((addr), (LiteralArg (s))))::acc))
     end
| FromNode (up) -> begin
     (match ((assoc up results)) with
| FStar_Pervasives_Native.None -> begin
     Error (EvalIllTyped (PipelineForwardEdge (nid, addr, up)))
     end
| FStar_Pervasives_Native.Some (x) -> begin
     (match ((arg_space_of lk cap_id addr)) with
| FStar_Pervasives_Native.None -> begin
     Error (EvalIllTyped (PipelineArgRefused (nid, UninvocableArg (addr))))
     end
| FStar_Pervasives_Native.Some (space) -> begin
     (

let spelled = (spell1 x)
in  
if (validate rd space spelled) then begin
     (resolve_args rd lk spell1 results nid cap_id rest ((((addr), (FromUpstream (x))))::acc))
     end else begin
     Error (EvalArgRefused (nid, ArgOutOfSpace (addr, space, spelled)))
     end)
     end)
     end)
     end)
     end))


let run_node = (fun ( rd  :  readers ) ( lk  :  capability_lookup ) ( spell1  :  'v  ->  Prims.string ) ( body  :  node_body<'v> ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( n  :  pipeline_node ) -> (

let resolved = (match (n) with
| Source (uu___, uu___1, uu___2) -> begin
     Ok ([])
     end
| Invoke (nid, cap_id, uu___, a) -> begin
     (match ((resolve_args rd lk spell1 results nid cap_id a [])) with
| Ok (xs) -> begin
     Ok ((rev xs))
     end
| Error (e) -> begin
     Error (e)
     end)
     end)
in (match (resolved) with
| Error (e) -> begin
     Error (e)
     end
| Ok (xs) -> begin
     (match ((body n xs)) with
| Ok (x) -> begin
     Ok (x)
     end
| Error (m) -> begin
     Error (EvalNodeFailed ((node_id n), m))
     end)
     end)))


let rec eval_go = (fun ( rd  :  readers ) ( lk  :  capability_lookup ) ( spell1  :  'v  ->  Prims.string ) ( body  :  node_body<'v> ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( ns  :  Prims.list<pipeline_node> ) -> (match (ns) with
| [] -> begin
     Ok (results)
     end
| (n)::rest -> begin
     (match ((run_node rd lk spell1 body results n)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (x) -> begin
     (eval_go rd lk spell1 body (((((node_id n)), (x)))::results) rest)
     end)
     end))


let eval = (fun ( fr  :  feed_readers ) ( rd  :  readers ) ( lk  :  capability_lookup ) ( spell1  :  'v  ->  Prims.string ) ( body  :  node_body<'v> ) ( p  :  pipeline ) -> (match ((type_check fr rd lk p)) with
| Error (e) -> begin
     Error (EvalIllTyped (e))
     end
| Ok (()) -> begin
     (eval_go rd lk spell1 body [] p.p_nodes)
     end))


type dmap = Prims.list<(Prims.string * Prims.list<Prims.string>)>


let rec deps_of : Prims.list<pipeline_node>  ->  dmap = (fun ( ns  :  Prims.list<pipeline_node> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     ((((node_id n)), ((upstreams n))))::(deps_of t)
     end))


let rec edge : dmap  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( deps  :  dmap ) ( n  :  Prims.string ) ( r  :  Prims.string ) -> (match (deps) with
| [] -> begin
     false
     end
| ((k, reads))::t -> begin
     (((Prims.op_Equals k n) && (mem r reads)) || (edge t n r))
     end))


let rec pairs_of : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( nd  :  Prims.string ) ( reads  :  Prims.list<Prims.string> ) -> (match (reads) with
| [] -> begin
     []
     end
| (r)::t -> begin
     (((r), (nd)))::(pairs_of nd t)
     end))


let rec pairs : dmap  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( deps  :  dmap ) -> (match (deps) with
| [] -> begin
     []
     end
| ((nd, reads))::t -> begin
     (app (pairs_of nd reads) (pairs t))
     end))


let rec firsts : Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<Prims.string> = (fun ( ps  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (ps) with
| [] -> begin
     []
     end
| ((r, uu___))::t -> begin
     (r)::(firsts t)
     end))


let rec seconds_for : Prims.string  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<Prims.string> = (fun ( k  :  Prims.string ) ( ps  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (ps) with
| [] -> begin
     []
     end
| ((r, n))::t -> begin
      
if (Prims.op_Equals r k) then begin
     (n)::(seconds_for k t)
     end else begin
     (seconds_for k t)
     end
     end))


let rec group_from : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)>  ->  dmap = (fun ( ks  :  Prims.list<Prims.string> ) ( ps  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (ks) with
| [] -> begin
     []
     end
| (k)::t -> begin
     (((k), ((dedup (seconds_for k ps)))))::(group_from t ps)
     end))


let dependents : dmap  ->  dmap = (fun ( deps  :  dmap ) -> (

let ps = (pairs deps)
in (group_from (dedup (firsts ps)) ps)))


let dependents_of : dmap  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( d  :  dmap ) ( nd  :  Prims.string ) -> (match ((assoc nd d)) with
| FStar_Pervasives_Native.Some (ds) -> begin
     ds
     end
| FStar_Pervasives_Native.None -> begin
     []
     end))


let rec mem_pair : Prims.string  ->  Prims.string  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.bool = (fun ( r  :  Prims.string ) ( n  :  Prims.string ) ( ps  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (ps) with
| [] -> begin
     false
     end
| ((r', n'))::t -> begin
     (((Prims.op_Equals r r') && (Prims.op_Equals n n')) || (mem_pair r n t))
     end))


let rec next_of : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( d  :  dmap ) ( frontier  :  Prims.list<Prims.string> ) ( s  :  Prims.list<Prims.string> ) -> (match (frontier) with
| [] -> begin
     s
     end
| (nd)::t -> begin
     (next_of d t (match ((assoc nd d)) with
| FStar_Pervasives_Native.Some (ds) -> begin
     (union s ds)
     end
| FStar_Pervasives_Native.None -> begin
     s
     end))
     end))


let rec any_dep : dmap  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( d  :  dmap ) ( frontier  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (match (frontier) with
| [] -> begin
     false
     end
| (nd)::t -> begin
     ((mem x (dependents_of d nd)) || (any_dep d t x))
     end))


let rec range : dmap  ->  Prims.list<Prims.string> = (fun ( d  :  dmap ) -> (match (d) with
| [] -> begin
     []
     end
| ((uu___, vs))::t -> begin
     (app vs (range t))
     end))


let rec grow : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( d  :  dmap ) ( frontier  :  Prims.list<Prims.string> ) ( acc  :  Prims.list<Prims.string> ) -> (match (frontier) with
| [] -> begin
     acc
     end
| (uu___)::uu___1 -> begin
     (

let next = (next_of d frontier [])
in (

let fresh = (diff next acc)
in (grow d fresh (union acc fresh))))
     end))


let dirty_set : Prims.list<Prims.string>  ->  pipeline  ->  Prims.list<Prims.string> = (fun ( changed  :  Prims.list<Prims.string> ) ( p  :  pipeline ) -> (grow (dependents (deps_of p.p_nodes)) changed changed))


let rec eval_from_go = (fun ( rd  :  readers ) ( lk  :  capability_lookup ) ( spell1  :  'v  ->  Prims.string ) ( body  :  node_body<'v> ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( dirty  :  Prims.list<Prims.string> ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( ns  :  Prims.list<pipeline_node> ) -> (match (ns) with
| [] -> begin
     Ok (results)
     end
| (n)::rest -> begin
     (

let nid = (node_id n)
in (match ( 
if (mem nid dirty) then begin
     FStar_Pervasives_Native.None
     end else begin
     (assoc nid prior)
     end) with
| FStar_Pervasives_Native.Some (x) -> begin
     (eval_from_go rd lk spell1 body prior dirty ((((nid), (x)))::results) rest)
     end
| FStar_Pervasives_Native.None -> begin
     (match ((run_node rd lk spell1 body results n)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (x) -> begin
     (eval_from_go rd lk spell1 body prior dirty ((((nid), (x)))::results) rest)
     end)
     end))
     end))


let eval_from = (fun ( fr  :  feed_readers ) ( rd  :  readers ) ( lk  :  capability_lookup ) ( spell1  :  'v  ->  Prims.string ) ( body  :  node_body<'v> ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( p  :  pipeline ) -> (match ((type_check fr rd lk p)) with
| Error (e) -> begin
     Error (EvalIllTyped (e))
     end
| Ok (()) -> begin
     (eval_from_go rd lk spell1 body prior (dirty_set changed p) [] p.p_nodes)
     end))


let rec ordered : Prims.list<Prims.string>  ->  Prims.list<pipeline_node>  ->  Prims.bool = (fun ( earlier  :  Prims.list<Prims.string> ) ( ns  :  Prims.list<pipeline_node> ) -> (match (ns) with
| [] -> begin
     true
     end
| (n)::rest -> begin
     (((not ((mem (node_id n) earlier))) && (subset (upstreams n) earlier)) && (ordered (app earlier (((node_id n))::[])) rest))
     end))

type jval =
| JStr of Prims.string
| JInt of Prims.int
| JBool of Prims.bool
| JFloat of Prims.string
| JArr of Prims.list<jval>
| JObj of Prims.list<(Prims.string * jval)>


let uu___is_JStr : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JStr (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JStr__item___0 : jval  ->  Prims.string = (fun ( projectee  :  jval ) -> (match (projectee) with
| JStr (_0) -> begin
     _0
     end))


let uu___is_JInt : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JInt (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JInt__item___0 : jval  ->  Prims.int = (fun ( projectee  :  jval ) -> (match (projectee) with
| JInt (_0) -> begin
     _0
     end))


let uu___is_JBool : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JBool (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JBool__item___0 : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JBool (_0) -> begin
     _0
     end))


let uu___is_JFloat : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JFloat (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JFloat__item___0 : jval  ->  Prims.string = (fun ( projectee  :  jval ) -> (match (projectee) with
| JFloat (_0) -> begin
     _0
     end))


let uu___is_JArr : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JArr (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JArr__item___0 : jval  ->  Prims.list<jval> = (fun ( projectee  :  jval ) -> (match (projectee) with
| JArr (_0) -> begin
     _0
     end))


let uu___is_JObj : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JObj (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JObj__item___0 : jval  ->  Prims.list<(Prims.string * jval)> = (fun ( projectee  :  jval ) -> (match (projectee) with
| JObj (_0) -> begin
     _0
     end))

type path_seg =
| Key of Prims.string
| Index of Prims.nat


let uu___is_Key : path_seg  ->  Prims.bool = (fun ( projectee  :  path_seg ) -> (match (projectee) with
| Key (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Key__item___0 : path_seg  ->  Prims.string = (fun ( projectee  :  path_seg ) -> (match (projectee) with
| Key (_0) -> begin
     _0
     end))


let uu___is_Index : path_seg  ->  Prims.bool = (fun ( projectee  :  path_seg ) -> (match (projectee) with
| Index (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Index__item___0 : path_seg  ->  Prims.nat = (fun ( projectee  :  path_seg ) -> (match (projectee) with
| Index (_0) -> begin
     _0
     end))

type decode_code =
| InvalidJson
| MissingField
| WrongKind
| UnknownTag
| OutOfRange
| UndeclaredMember
| LimitExceeded
| NotAdmitted
| SchemaFault


let uu___is_InvalidJson : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| InvalidJson -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_MissingField : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| MissingField -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_WrongKind : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| WrongKind -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UnknownTag : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| UnknownTag -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OutOfRange : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| OutOfRange -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UndeclaredMember : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| UndeclaredMember -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_LimitExceeded : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| LimitExceeded -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_NotAdmitted : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| NotAdmitted -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SchemaFault : decode_code  ->  Prims.bool = (fun ( projectee  :  decode_code ) -> (match (projectee) with
| SchemaFault -> begin
     true
     end
| uu___ -> begin
     false
     end))

type decode_error = {d_code : decode_code; d_path : Prims.list<path_seg>}


let __proj__Mkdecode_error__item__d_code : decode_error  ->  decode_code = (fun ( projectee  :  decode_error ) -> (match (projectee) with
| {d_code = d_code; d_path = d_path} -> begin
     d_code
     end))


let __proj__Mkdecode_error__item__d_path : decode_error  ->  Prims.list<path_seg> = (fun ( projectee  :  decode_error ) -> (match (projectee) with
| {d_code = d_code; d_path = d_path} -> begin
     d_path
     end))


type decoder<'a> = jval  ->  outcome<'a, decode_error>


let refuse : decode_code  ->  decode_error = (fun ( c  :  decode_code ) -> {d_code = c; d_path = []})


let under : path_seg  ->  decode_error  ->  decode_error = (fun ( s  :  path_seg ) ( e  :  decode_error ) -> {d_code = e.d_code; d_path = (s)::e.d_path})

type codec_readers = {float_of_int : Prims.int  ->  Prims.string}


let __proj__Mkcodec_readers__item__float_of_int : codec_readers  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  codec_readers ) -> (match (projectee) with
| {float_of_int = float_of_int} -> begin
     float_of_int
     end))


let member1 : Prims.string  ->  jval  ->  FStar_Pervasives_Native.option<jval> = (fun ( name  :  Prims.string ) ( el  :  jval ) -> (match (el) with
| JObj (fields) -> begin
     (assoc name fields)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let d_str : jval  ->  outcome<Prims.string, decode_error> = (fun ( el  :  jval ) -> (match (el) with
| JStr (s) -> begin
     Ok (s)
     end
| uu___ -> begin
     Error ((refuse WrongKind))
     end))


let d_int : jval  ->  outcome<Prims.int, decode_error> = (fun ( el  :  jval ) -> (match (el) with
| JInt (i) -> begin
     Ok (i)
     end
| uu___ -> begin
     Error ((refuse WrongKind))
     end))


let d_bool : jval  ->  outcome<Prims.bool, decode_error> = (fun ( el  :  jval ) -> (match (el) with
| JBool (b) -> begin
     Ok (b)
     end
| uu___ -> begin
     Error ((refuse WrongKind))
     end))


let d_float : codec_readers  ->  jval  ->  outcome<Prims.string, decode_error> = (fun ( cr  :  codec_readers ) ( el  :  jval ) -> (match (el) with
| JFloat (f) -> begin
     Ok (f)
     end
| JInt (i) -> begin
     Ok ((cr.float_of_int i))
     end
| uu___ -> begin
     Error ((refuse WrongKind))
     end))


let field = (fun ( name  :  Prims.string ) ( d  :  decoder<'a> ) ( el  :  jval ) -> (match (el) with
| JObj (fields) -> begin
     (match ((assoc name fields)) with
| FStar_Pervasives_Native.Some (x) -> begin
     (match ((d x)) with
| Ok (y) -> begin
     Ok (y)
     end
| Error (e) -> begin
     Error ((under (Key (name)) e))
     end)
     end
| FStar_Pervasives_Native.None -> begin
     Error ({d_code = MissingField; d_path = (Key (name))::[]})
     end)
     end
| uu___ -> begin
     Error ((refuse WrongKind))
     end))


let opt_field = (fun ( name  :  Prims.string ) ( d  :  decoder<'a> ) ( el  :  jval ) -> (match (el) with
| JObj (fields) -> begin
     (match ((assoc name fields)) with
| FStar_Pervasives_Native.Some (x) -> begin
     (match ((d x)) with
| Ok (y) -> begin
     Ok (FStar_Pervasives_Native.Some (y))
     end
| Error (e) -> begin
     Error ((under (Key (name)) e))
     end)
     end
| FStar_Pervasives_Native.None -> begin
     Ok (FStar_Pervasives_Native.None)
     end)
     end
| uu___ -> begin
     Error ((refuse WrongKind))
     end))


let rec list_from = (fun ( d  :  decoder<'a> ) ( i  :  Prims.nat ) ( xs  :  Prims.list<jval> ) -> (match (xs) with
| [] -> begin
     Ok ([])
     end
| (x)::rest -> begin
     (match ((d x)) with
| Error (e) -> begin
     Error ((under (Index (i)) e))
     end
| Ok (y) -> begin
     (match ((list_from d (i + (Prims.parse_int "1")) rest)) with
| Ok (ys) -> begin
     Ok ((y)::ys)
     end
| Error (e) -> begin
     Error (e)
     end)
     end)
     end))


let d_list = (fun ( d  :  decoder<'a> ) ( el  :  jval ) -> (match (el) with
| JArr (xs) -> begin
     (list_from d (Prims.parse_int "0") xs)
     end
| uu___ -> begin
     Error ((refuse WrongKind))
     end))


let rec strs : Prims.list<Prims.string>  ->  Prims.list<jval> = (fun ( xs  :  Prims.list<Prims.string> ) -> (match (xs) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (JStr (x))::(strs t)
     end))


let space_json : value_space  ->  jval = (fun ( s  :  value_space ) -> (match (s) with
| IntRange (lo, hi) -> begin
     JObj (((("$type"), (JStr ("intRange"))))::((("min"), (JInt (lo))))::((("max"), (JInt (hi))))::[])
     end
| FloatRange (lo, hi) -> begin
     JObj (((("$type"), (JStr ("floatRange"))))::((("min"), (JFloat (lo))))::((("max"), (JFloat (hi))))::[])
     end
| StringLen (lo, hi) -> begin
     JObj (((("$type"), (JStr ("stringLen"))))::((("min"), (JInt (lo))))::((("max"), (JInt (hi))))::[])
     end
| Enum (xs) -> begin
     JObj (((("$type"), (JStr ("enum"))))::((("values"), (JArr ((strs xs)))))::[])
     end
| AnyString -> begin
     JObj (((("$type"), (JStr ("anyString"))))::[])
     end
| SlotTree (c) -> begin
     JObj (((("$type"), (JStr ("slotTree"))))::(match (c) with
| FStar_Pervasives_Native.Some (k) -> begin
     ((("slotKind"), (JStr (k))))::[]
     end
| FStar_Pervasives_Native.None -> begin
     []
     end))
     end))


let space_cases : codec_readers  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  jval  ->  outcome<value_space, decode_error> = (fun ( cr  :  codec_readers ) ( key  :  Prims.string ) ( len_lo  :  Prims.string ) ( len_hi  :  Prims.string ) ( el  :  jval ) -> (match ((field key d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (t) -> begin
      
if (Prims.op_Equals t "intRange") then begin
     (match ((field "min" d_int el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (lo) -> begin
     (match ((field "max" d_int el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (hi) -> begin
     Ok (IntRange (lo, hi))
     end)
     end)
     end else begin
      
if (Prims.op_Equals t "floatRange") then begin
     (match ((field "min" (d_float cr) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (lo) -> begin
     (match ((field "max" (d_float cr) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (hi) -> begin
     Ok (FloatRange (lo, hi))
     end)
     end)
     end else begin
      
if (Prims.op_Equals t "stringLen") then begin
     (match ((field len_lo d_int el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (lo) -> begin
     (match ((field len_hi d_int el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (hi) -> begin
     Ok (StringLen (lo, hi))
     end)
     end)
     end else begin
      
if (Prims.op_Equals t "enum") then begin
     (match ((field "values" (d_list d_str) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (xs) -> begin
     Ok (Enum (xs))
     end)
     end else begin
      
if (Prims.op_Equals t "anyString") then begin
     Ok (AnyString)
     end else begin
      
if (Prims.op_Equals t "slotTree") then begin
     (match ((opt_field "slotKind" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (c) -> begin
     Ok (SlotTree (c))
     end)
     end else begin
     Error ((under (Key (key)) (refuse UnknownTag)))
     end
     end
     end
     end
     end
     end
     end))


let space_of_j : codec_readers  ->  jval  ->  outcome<value_space, decode_error> = (fun ( cr  :  codec_readers ) ( el  :  jval ) -> (match ((((member1 "$type" el)), ((member1 "kind" el)))) with
| (FStar_Pervasives_Native.None, FStar_Pervasives_Native.Some (uu___)) -> begin
     (space_cases cr "kind" "minLength" "maxLength" el)
     end
| uu___ -> begin
     (space_cases cr "$type" "min" "max" el)
     end))


let host_tag : host_effect  ->  Prims.string = (fun ( h  :  host_effect ) -> (match (h) with
| Pure -> begin
     "pure"
     end
| ReadsHost -> begin
     "readsHost"
     end
| WritesHost -> begin
     "writesHost"
     end))


let host_of_j : jval  ->  outcome<host_effect, decode_error> = (fun ( el  :  jval ) -> (match ((d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (s) -> begin
      
if (Prims.op_Equals s "pure") then begin
     Ok (Pure)
     end else begin
      
if (Prims.op_Equals s "readsHost") then begin
     Ok (ReadsHost)
     end else begin
      
if (Prims.op_Equals s "writesHost") then begin
     Ok (WritesHost)
     end else begin
     Error ((refuse UnknownTag))
     end
     end
     end
     end))


let det_of_j : jval  ->  outcome<determinism_source, decode_error> = (fun ( el  :  jval ) -> (match ((d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (tag) -> begin
     (match ((det_of_tag tag)) with
| FStar_Pervasives_Native.Some (d) -> begin
     Ok (d)
     end
| FStar_Pervasives_Native.None -> begin
     Error ((refuse UnknownTag))
     end)
     end))


let effect_json : effect_class  ->  jval = (fun ( e  :  effect_class ) -> JObj (((("host"), (JStr ((host_tag e.host)))))::((("determinism"), (JStr ((determinism_tag e.determinism)))))::[]))


let effect_of_j : jval  ->  outcome<effect_class, decode_error> = (fun ( el  :  jval ) -> (match ((field "host" host_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (h) -> begin
     (match ((field "determinism" det_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (d) -> begin
     Ok ({host = h; determinism = d})
     end)
     end))


let hole_tags : Prims.list<Prims.string> = ("value")::("slot")::("repeat")::("action")::[]


let derived_slot_space : sig_entry  ->  Prims.bool = (fun ( e  :  sig_entry ) -> ((Prims.op_Equals e.s_kind "slot") && (Prims.op_Equals e.s_space (FStar_Pervasives_Native.Some (SlotTree (e.s_slot))))))


let space_members : sig_entry  ->  Prims.list<(Prims.string * jval)> = (fun ( e  :  sig_entry ) -> (match (e.s_space) with
| FStar_Pervasives_Native.Some (s) -> begin
      
if (derived_slot_space e) then begin
     []
     end else begin
     ((("space"), ((space_json s))))::[]
     end
     end
| FStar_Pervasives_Native.None -> begin
     []
     end))


let slot_members : sig_entry  ->  Prims.list<(Prims.string * jval)> = (fun ( e  :  sig_entry ) -> (match (e.s_slot) with
| FStar_Pervasives_Native.Some (k) -> begin
     ((("slotKind"), (JStr (k))))::[]
     end
| FStar_Pervasives_Native.None -> begin
     []
     end))


let action_members : sig_entry  ->  Prims.list<(Prims.string * jval)> = (fun ( e  :  sig_entry ) -> (match (e.s_action) with
| FStar_Pervasives_Native.Some (eff) -> begin
     ((("actionEffect"), ((effect_json eff))))::[]
     end
| FStar_Pervasives_Native.None -> begin
     []
     end))


let entry_fields : sig_entry  ->  Prims.list<(Prims.string * jval)> = (fun ( e  :  sig_entry ) -> ((("addr"), (JStr (e.s_addr))))::((("name"), (JStr (e.s_name))))::((("kind"), (JStr (e.s_kind))))::((("required"), (JBool (e.s_required))))::(app (space_members e) (app (slot_members e) (action_members e))))


let entry_json : sig_entry  ->  jval = (fun ( e  :  sig_entry ) -> JObj ((entry_fields e)))


let kind_of_j : jval  ->  outcome<Prims.string, decode_error> = (fun ( el  :  jval ) -> (match ((d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (s) -> begin
      
if (mem s hole_tags) then begin
     Ok (s)
     end else begin
     Error ((refuse UnknownTag))
     end
     end))


let entry_of_j : codec_readers  ->  jval  ->  outcome<sig_entry, decode_error> = (fun ( cr  :  codec_readers ) ( el  :  jval ) -> (match ((field "addr" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (addr) -> begin
     (match ((field "name" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (name) -> begin
     (match ((field "kind" kind_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (kind) -> begin
     (match ((field "required" d_bool el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (required) -> begin
     (match ((opt_field "space" (space_of_j cr) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (sp) -> begin
     (match ((opt_field "actionEffect" effect_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (ac) -> begin
     (match ((opt_field "slotKind" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (slot) -> begin
     (

let sp' = (match (sp) with
| FStar_Pervasives_Native.None -> begin
      
if (Prims.op_Equals kind "slot") then begin
     FStar_Pervasives_Native.Some (SlotTree (slot))
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| FStar_Pervasives_Native.Some (s) -> begin
     FStar_Pervasives_Native.Some (s)
     end)
in Ok ({s_addr = addr; s_name = name; s_kind = kind; s_space = sp'; s_slot = slot; s_action = ac; s_required = required}))
     end)
     end)
     end)
     end)
     end)
     end)
     end))


let rec entries_json : Prims.list<sig_entry>  ->  Prims.list<jval> = (fun ( es  :  Prims.list<sig_entry> ) -> (match (es) with
| [] -> begin
     []
     end
| (e)::t -> begin
     ((entry_json e))::(entries_json t)
     end))


let signature_json : signature  ->  jval = (fun ( sg  :  signature ) -> JObj (((("name"), (JStr (sg.sg_name))))::((("effect"), ((effect_json sg.sg_effect))))::((("holes"), (JArr ((entries_json sg.sg_holes)))))::[]))


let signature_of_j : readers  ->  codec_readers  ->  jval  ->  outcome<signature, decode_error> = (fun ( rd  :  readers ) ( cr  :  codec_readers ) ( el  :  jval ) -> (match ((field "name" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (name) -> begin
     (match ((field "effect" effect_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (eff) -> begin
     (match ((field "holes" (d_list (entry_of_j cr)) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (holes) -> begin
     (

let sg = {sg_name = name; sg_holes = holes; sg_effect = eff}
in (match ((validate_signature rd sg)) with
| FStar_Pervasives_Native.None -> begin
     Ok (sg)
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     Error ({d_code = OutOfRange; d_path = (Key ("holes"))::[]})
     end))
     end)
     end)
     end))


let island_tag : island_kind  ->  Prims.string = (fun ( k  :  island_kind ) -> (match (k) with
| Pyodide -> begin
     "pyodide"
     end
| Fable -> begin
     "fable"
     end
| Js -> begin
     "js"
     end))


let island_of_j : jval  ->  outcome<island_kind, decode_error> = (fun ( el  :  jval ) -> (match ((d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (s) -> begin
      
if (Prims.op_Equals s "pyodide") then begin
     Ok (Pyodide)
     end else begin
      
if (Prims.op_Equals s "fable") then begin
     Ok (Fable)
     end else begin
      
if (Prims.op_Equals s "js") then begin
     Ok (Js)
     end else begin
     Error ((refuse UnknownTag))
     end
     end
     end
     end))


let placement_json : placement  ->  jval = (fun ( p  :  placement ) -> (match (p) with
| BuildTime -> begin
     JObj (((("$type"), (JStr ("buildTime"))))::[])
     end
| Server -> begin
     JObj (((("$type"), (JStr ("server"))))::[])
     end
| ClientDeclarative -> begin
     JObj (((("$type"), (JStr ("clientDeclarative"))))::[])
     end
| Precomputed -> begin
     JObj (((("$type"), (JStr ("precomputed"))))::[])
     end
| ClientIsland (k) -> begin
     JObj (((("$type"), (JStr ("clientIsland"))))::((("island"), (JStr ((island_tag k)))))::[])
     end))


let placement_of_j : jval  ->  outcome<placement, decode_error> = (fun ( el  :  jval ) -> (match ((field "$type" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (t) -> begin
      
if (Prims.op_Equals t "buildTime") then begin
     Ok (BuildTime)
     end else begin
      
if (Prims.op_Equals t "server") then begin
     Ok (Server)
     end else begin
      
if (Prims.op_Equals t "clientDeclarative") then begin
     Ok (ClientDeclarative)
     end else begin
      
if (Prims.op_Equals t "precomputed") then begin
     Ok (Precomputed)
     end else begin
      
if (Prims.op_Equals t "clientIsland") then begin
     (match ((field "island" island_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (k) -> begin
     Ok (ClientIsland (k))
     end)
     end else begin
     Error ((under (Key ("$type")) (refuse UnknownTag)))
     end
     end
     end
     end
     end
     end))


let capability_json : capability  ->  jval = (fun ( c  :  capability ) -> JObj (((("$type"), (JStr ("capability"))))::((("id"), (JStr (c.c_id))))::((("signature"), ((signature_json c.c_signature))))::((("determinism"), (JStr ((determinism_tag c.c_determinism)))))::((("placement"), ((placement_json c.c_placement))))::[]))


let doc_tag_of_j : jval  ->  outcome<unit, decode_error> = (fun ( el  :  jval ) -> (match ((d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (s) -> begin
      
if (Prims.op_Equals s "capability") then begin
     Ok (())
     end else begin
     Error ((refuse UnknownTag))
     end
     end))


let det_agrees_j : Prims.string  ->  jval  ->  outcome<unit, decode_error> = (fun ( expected  :  Prims.string ) ( el  :  jval ) -> (match ((d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (wire1) -> begin
      
if (Prims.op_Less_Greater wire1 expected) then begin
     Error ((refuse OutOfRange))
     end else begin
     Ok (())
     end
     end))


let capability_of_j : readers  ->  codec_readers  ->  jval  ->  outcome<capability, decode_error> = (fun ( rd  :  readers ) ( cr  :  codec_readers ) ( el  :  jval ) -> (match ((field "$type" doc_tag_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((field "id" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (id) -> begin
     (match ((field "signature" (signature_of_j rd cr) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (sg) -> begin
      
if (not ((is_total sg))) then begin
     Error ({d_code = OutOfRange; d_path = (Key ("signature"))::(Key ("holes"))::[]})
     end else begin
     (match ((field "determinism" (det_agrees_j (determinism_tag sg.sg_effect.determinism)) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (match ((field "placement" placement_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (pl) -> begin
     Ok ({c_id = id; c_signature = sg; c_determinism = sg.sg_effect.determinism; c_placement = pl})
     end)
     end)
     end
     end)
     end)
     end))


let arg_source_json : arg_source  ->  jval = (fun ( s  :  arg_source ) -> (match (s) with
| Literal (x) -> begin
     JObj (((("$type"), (JStr ("literal"))))::((("value"), (JStr (x))))::[])
     end
| FromNode (n) -> begin
     JObj (((("$type"), (JStr ("fromNode"))))::((("node"), (JStr (n))))::[])
     end))


let arg_source_of_j : jval  ->  outcome<arg_source, decode_error> = (fun ( el  :  jval ) -> (match ((field "$type" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (t) -> begin
      
if (Prims.op_Equals t "literal") then begin
     (match ((field "value" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (x) -> begin
     Ok (Literal (x))
     end)
     end else begin
      
if (Prims.op_Equals t "fromNode") then begin
     (match ((field "node" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (n) -> begin
     Ok (FromNode (n))
     end)
     end else begin
     Error ((under (Key ("$type")) (refuse UnknownTag)))
     end
     end
     end))


let arg_json : (Prims.string * arg_source)  ->  jval = (fun ( b  :  (Prims.string * arg_source) ) -> JObj (((("addr"), (JStr ((FStar_Pervasives_Native.fst b)))))::((("source"), ((arg_source_json (FStar_Pervasives_Native.snd b)))))::[]))


let arg_of_j : jval  ->  outcome<(Prims.string * arg_source), decode_error> = (fun ( el  :  jval ) -> (match ((field "addr" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (addr) -> begin
     (match ((field "source" arg_source_of_j el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (s) -> begin
     Ok (((addr), (s)))
     end)
     end))


let rec args_json : Prims.list<(Prims.string * arg_source)>  ->  Prims.list<jval> = (fun ( a  :  Prims.list<(Prims.string * arg_source)> ) -> (match (a) with
| [] -> begin
     []
     end
| (b)::t -> begin
     ((arg_json b))::(args_json t)
     end))


let out_space_of_j : readers  ->  codec_readers  ->  jval  ->  outcome<value_space, decode_error> = (fun ( rd  :  readers ) ( cr  :  codec_readers ) ( el  :  jval ) -> (match ((space_of_j cr el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (sp) -> begin
     (match ((space_wf rd sp)) with
| FStar_Pervasives_Native.None -> begin
     Ok (sp)
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     Error ((refuse OutOfRange))
     end)
     end))


let node_json : pipeline_node  ->  jval = (fun ( n  :  pipeline_node ) -> (match (n) with
| Source (id, dref, ty) -> begin
     JObj (((("$type"), (JStr ("source"))))::((("id"), (JStr (id))))::((("dataRef"), (JStr (dref))))::((("outputType"), ((space_json ty))))::[])
     end
| Invoke (id, cap_id, ty, a) -> begin
     JObj (((("$type"), (JStr ("invoke"))))::((("id"), (JStr (id))))::((("capabilityId"), (JStr (cap_id))))::((("outputType"), ((space_json ty))))::((("args"), (JArr ((args_json a)))))::[])
     end))


let node_of_j : readers  ->  codec_readers  ->  jval  ->  outcome<pipeline_node, decode_error> = (fun ( rd  :  readers ) ( cr  :  codec_readers ) ( el  :  jval ) -> (match ((field "$type" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (t) -> begin
      
if (Prims.op_Equals t "source") then begin
     (match ((field "id" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (id) -> begin
     (match ((field "dataRef" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (dref) -> begin
     (match ((field "outputType" (out_space_of_j rd cr) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (ty) -> begin
     Ok (Source (id, dref, ty))
     end)
     end)
     end)
     end else begin
      
if (Prims.op_Equals t "invoke") then begin
     (match ((field "id" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (id) -> begin
     (match ((field "capabilityId" d_str el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (cap_id) -> begin
     (match ((field "outputType" (out_space_of_j rd cr) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (ty) -> begin
     (match ((field "args" (d_list arg_of_j) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (a) -> begin
     Ok (Invoke (id, cap_id, ty, a))
     end)
     end)
     end)
     end)
     end else begin
     Error ((under (Key ("$type")) (refuse UnknownTag)))
     end
     end
     end))


let rec nodes_json : Prims.list<pipeline_node>  ->  Prims.list<jval> = (fun ( ns  :  Prims.list<pipeline_node> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     ((node_json n))::(nodes_json t)
     end))


let pipeline_json : pipeline  ->  jval = (fun ( p  :  pipeline ) -> JObj (((("nodes"), (JArr ((nodes_json p.p_nodes)))))::[]))


let pipeline_of_j : readers  ->  codec_readers  ->  jval  ->  outcome<pipeline, decode_error> = (fun ( rd  :  readers ) ( cr  :  codec_readers ) ( el  :  jval ) -> (match ((field "nodes" (d_list (node_of_j rd cr)) el)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (ns) -> begin
     Ok ({p_nodes = ns})
     end))


let normal_entry : sig_entry  ->  sig_entry = (fun ( e  :  sig_entry ) -> {s_addr = e.s_addr; s_name = e.s_name; s_kind = e.s_kind; s_space = (arg_space e); s_slot = e.s_slot; s_action = e.s_action; s_required = e.s_required})


let canonical_entry : sig_entry  ->  Prims.bool = (fun ( e  :  sig_entry ) -> ((mem e.s_kind hole_tags) && (Prims.op_Equals (arg_space e) e.s_space)))


let rec first_bad_kind : Prims.nat  ->  Prims.list<sig_entry>  ->  FStar_Pervasives_Native.option<Prims.nat> = (fun ( i  :  Prims.nat ) ( es  :  Prims.list<sig_entry> ) -> (match (es) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (e)::t -> begin
      
if (mem e.s_kind hole_tags) then begin
     (first_bad_kind (i + (Prims.parse_int "1")) t)
     end else begin
     FStar_Pervasives_Native.Some (i)
     end
     end))


let normal_signature : signature  ->  signature = (fun ( sg  :  signature ) -> {sg_name = sg.sg_name; sg_holes = (map normal_entry sg.sg_holes); sg_effect = sg.sg_effect})


let wf_signature : readers  ->  signature  ->  Prims.bool = (fun ( rd  :  readers ) ( sg  :  signature ) -> ((for_all canonical_entry sg.sg_holes) && (match ((validate_signature rd sg)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end)))


let wf_capability : readers  ->  capability  ->  Prims.bool = (fun ( rd  :  readers ) ( c  :  capability ) -> (((wf_signature rd c.c_signature) && (is_total c.c_signature)) && (Prims.op_Equals c.c_determinism c.c_signature.sg_effect.determinism)))


let rec first_bad_node : readers  ->  Prims.nat  ->  Prims.list<pipeline_node>  ->  FStar_Pervasives_Native.option<Prims.nat> = (fun ( rd  :  readers ) ( i  :  Prims.nat ) ( ns  :  Prims.list<pipeline_node> ) -> (match (ns) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (n)::t -> begin
      
if (match ((space_wf rd (node_output n))) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     (first_bad_node rd (i + (Prims.parse_int "1")) t)
     end else begin
     FStar_Pervasives_Native.Some (i)
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


let twins : Prims.list<twin> = ({tname = "determinism-tag-of-clock-and-network"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (determinism_tag {has_clock = true; has_random = false; has_network = true}) "clock+network"))})::({tname = "det-of-tag-reads-its-canonical-order"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (det_of_tag "random+network") (FStar_Pervasives_Native.Some ({has_clock = false; has_random = true; has_network = true}))))})::({tname = "det-of-tag-refuses-another-order"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (det_of_tag "network+random") FStar_Pervasives_Native.None))})::({tname = "a-bounded-repeat-is-required"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (entry_of {h_addr = "r"; h_name = "r"; h_kind = RepeatHole (IntRange ((Prims.parse_int "0"), (Prims.parse_int "3")))}).s_required true))})::({tname = "an-unbounded-repeat-is-not-required"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (entry_of {h_addr = "r"; h_name = "r"; h_kind = RepeatHole (AnyString)}).s_required false))})::({tname = "an-entry-of-no-hole-kind-is-not-total"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (entry_total {s_addr = "x"; s_name = "x"; s_kind = "int"; s_space = FStar_Pervasives_Native.Some (AnyString); s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.None; s_required = true}) false))})::({tname = "a-repeat-over-a-float-range-is-not-required"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (entry_of {h_addr = "r"; h_name = "r"; h_kind = RepeatHole (FloatRange ("0", "1"))}).s_required false))})::({tname = "a-repeat-past-the-cap-is-not-a-count"; tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (is_count (IntRange ((Prims.parse_int "0"), (Prims.parse_int "1000001")))) false) && (Prims.op_Equals (is_count (IntRange ((Prims.parse_int "0"), (Prims.parse_int "1000000")))) true)))})::({tname = "repeated-names-each-repeat-in-order"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (repeated [] (("a")::("b")::("a")::("b")::("a")::[])) (("a")::("b")::("a")::[])))})::({tname = "a-second-address-is-the-duplicate"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (validate_entries {int_of = (fun ( uu___1  :  Prims.string ) -> FStar_Pervasives_Native.None); float_in = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  Prims.string ) -> false); str_len = (fun ( uu___1  :  Prims.string ) -> (Prims.parse_int "0")); kind_of = (fun ( uu___1  :  Prims.string ) -> FStar_Pervasives_Native.None); float_fault = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) -> FStar_Pervasives_Native.None)} [] (({s_addr = "a"; s_name = "a"; s_kind = "value"; s_space = FStar_Pervasives_Native.Some (IntRange ((Prims.parse_int "0"), (Prims.parse_int "1"))); s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.None; s_required = true})::({s_addr = "a"; s_name = "b"; s_kind = "value"; s_space = FStar_Pervasives_Native.Some (IntRange ((Prims.parse_int "5"), (Prims.parse_int "1"))); s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.None; s_required = true})::[])) (FStar_Pervasives_Native.Some (DuplicateHoleAddr ("a")))))})::({tname = "map-of-list-keeps-the-later-binding-of-a-repeated-key"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (map_of_list (fun ( a  :  Prims.string ) ( b  :  Prims.string ) -> ((Prims.op_Equals a "a") || (Prims.op_Equals b "b"))) (((("b"), ((Prims.parse_int "1"))))::((("a"), ((Prims.parse_int "2"))))::((("b"), ((Prims.parse_int "3"))))::[])) (((("a"), ((Prims.parse_int "2"))))::((("b"), ((Prims.parse_int "3"))))::[])))})::({tname = "a-handler-key-that-is-no-hole-is-refused-naming-the-declared-actions"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (check_keys (("go")::[]) (("go")::("title")::[]) (((("stop"), ({hb_handler = (Prims.parse_int "0"); hb_effect = pure_deterministic})))::[])) (Error (UnknownActionAddr ("stop", ("go")::[])))))})::({tname = "a-handler-on-a-data-hole-is-not-an-action-hole"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (check_keys (("go")::[]) (("go")::("title")::[]) (((("title"), ({hb_handler = (Prims.parse_int "0"); hb_effect = pure_deterministic})))::[])) (Error (NotAnActionHole ("title")))))})::({tname = "a-handler-past-its-ceiling-is-refused-naming-both-effects"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (check_effects (((("go"), ({hb_handler = (Prims.parse_int "0"); hb_effect = {host = WritesHost; determinism = deterministic}})))::[]) (((("go"), (pure_deterministic)))::[])) (Error (HandlerEffectExceedsCeiling ("go", pure_deterministic, {host = WritesHost; determinism = deterministic})))))})::({tname = "first-dup-is-the-first-id-seen-again"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (first_dup (("a")::("b")::("c")::("b")::("a")::[])) (FStar_Pervasives_Native.Some ("a"))))})::({tname = "an-edge-that-closes-a-cycle-is-named-from-the-node"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (edge_fault {float_within = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  Prims.string ) ( uu___4  :  Prims.string ) -> false); int_within = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  Prims.int ) ( uu___4  :  Prims.int ) -> false)} {int_of = (fun ( uu___1  :  Prims.string ) -> FStar_Pervasives_Native.None); float_in = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  Prims.string ) -> false); str_len = (fun ( uu___1  :  Prims.string ) -> (Prims.parse_int "0")); kind_of = (fun ( uu___1  :  Prims.string ) -> FStar_Pervasives_Native.None); float_fault = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) -> FStar_Pervasives_Native.None)} ((Invoke ("a", "c", AnyString, ((("x"), (FromNode ("b"))))::[]))::(Invoke ("b", "c", AnyString, ((("x"), (FromNode ("a"))))::[]))::[]) (("a")::("b")::[]) "a" "x" "b" AnyString) (FStar_Pervasives_Native.Some (PipelineCycle ("a", ("a")::("b")::[])))))})::({tname = "a-later-upstream-that-closes-no-cycle-is-a-forward-edge"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (edge_fault {float_within = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  Prims.string ) ( uu___4  :  Prims.string ) -> false); int_within = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  Prims.int ) ( uu___4  :  Prims.int ) -> false)} {int_of = (fun ( uu___1  :  Prims.string ) -> FStar_Pervasives_Native.None); float_in = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  Prims.string ) -> false); str_len = (fun ( uu___1  :  Prims.string ) -> (Prims.parse_int "0")); kind_of = (fun ( uu___1  :  Prims.string ) -> FStar_Pervasives_Native.None); float_fault = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) -> FStar_Pervasives_Native.None)} ((Invoke ("a", "c", AnyString, ((("x"), (FromNode ("b"))))::[]))::(Source ("b", "ref", AnyString))::[]) (("a")::("b")::[]) "a" "x" "b" AnyString) (FStar_Pervasives_Native.Some (PipelineForwardEdge ("a", "x", "b")))))})::({tname = "the-dirty-set-is-the-change-and-everything-downstream"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (dirty_set (("s")::[]) {p_nodes = (Source ("s", "ref", AnyString))::(Invoke ("a", "c", AnyString, ((("x"), (FromNode ("s"))))::[]))::(Invoke ("b", "c", AnyString, ((("x"), (FromNode ("a"))))::[]))::(Source ("t", "ref", AnyString))::[]}) (("s")::("a")::("b")::[])))})::({tname = "an-entry-kind-outside-the-tags-is-refused-at-kind"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (entry_of_j {float_of_int = (fun ( uu___1  :  Prims.int ) -> "0")} (entry_json {s_addr = "x"; s_name = "x"; s_kind = "int"; s_space = FStar_Pervasives_Native.Some (AnyString); s_slot = FStar_Pervasives_Native.None; s_action = FStar_Pervasives_Native.None; s_required = true})) (Error ({d_code = UnknownTag; d_path = (Key ("kind"))::[]}))))})::({tname = "a-spaceless-slot-reads-back-with-its-derived-space"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (entry_of_j {float_of_int = (fun ( uu___1  :  Prims.int ) -> "0")} (entry_json {s_addr = "s"; s_name = "s"; s_kind = "slot"; s_space = FStar_Pervasives_Native.None; s_slot = FStar_Pervasives_Native.Some ("card"); s_action = FStar_Pervasives_Native.None; s_required = true})) (Ok ({s_addr = "s"; s_name = "s"; s_kind = "slot"; s_space = FStar_Pervasives_Native.Some (SlotTree (FStar_Pervasives_Native.Some ("card"))); s_slot = FStar_Pervasives_Native.Some ("card"); s_action = FStar_Pervasives_Native.None; s_required = true}))))})::({tname = "the-descriptor-spelling-of-a-space-is-read-leniently"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (space_of_j {float_of_int = (fun ( uu___1  :  Prims.int ) -> "0")} (JObj (((("kind"), (JStr ("stringLen"))))::((("minLength"), (JInt ((Prims.parse_int "1")))))::((("maxLength"), (JInt ((Prims.parse_int "3")))))::[]))) (Ok (StringLen ((Prims.parse_int "1"), (Prims.parse_int "3"))))))})::({tname = "a-node-with-an-empty-output-space-is-refused-at-outputType"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (node_of_j {int_of = (fun ( uu___1  :  Prims.string ) -> FStar_Pervasives_Native.None); float_in = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  Prims.string ) -> false); str_len = (fun ( uu___1  :  Prims.string ) -> (Prims.parse_int "0")); kind_of = (fun ( uu___1  :  Prims.string ) -> FStar_Pervasives_Native.None); float_fault = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) -> FStar_Pervasives_Native.None)} {float_of_int = (fun ( uu___1  :  Prims.int ) -> "0")} (node_json (Source ("s", "ref", IntRange ((Prims.parse_int "5"), (Prims.parse_int "1")))))) (Error ({d_code = OutOfRange; d_path = (Key ("outputType"))::[]}))))})::[]




