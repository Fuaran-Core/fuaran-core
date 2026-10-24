module DagFold

let rec concat = (fun ( ls  :  Prims.list<Prims.list<'a>> ) -> (match (ls) with
| [] -> begin
     []
     end
| (l)::t -> begin
     (ListAlgebra.app l (concat t))
     end))


let union = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (ListAlgebra.app x y))


let is_empty = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end))


let disjoint = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (is_empty (ListAlgebra.inter x y)))


let rec keep : (Prims.string  ->  Prims.bool)  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( f  :  Prims.string  ->  Prims.bool ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (f x) then begin
     (x)::(keep f t)
     end else begin
     (keep f t)
     end
     end))


type slot = (Prims.string * Prims.string)

type footprint = {reads : Prims.list<Prims.string>; structure_writes : Prims.list<Prims.string>; content_writes : Prims.list<Prims.string>; unknown_parent_writes : Prims.list<Prims.string>; slot_reads : Prims.list<slot>; slot_writes : Prims.list<slot>}


let __proj__Mkfootprint__item__reads : footprint  ->  Prims.list<Prims.string> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes; slot_reads = slot_reads; slot_writes = slot_writes} -> begin
     reads
     end))


let __proj__Mkfootprint__item__structure_writes : footprint  ->  Prims.list<Prims.string> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes; slot_reads = slot_reads; slot_writes = slot_writes} -> begin
     structure_writes
     end))


let __proj__Mkfootprint__item__content_writes : footprint  ->  Prims.list<Prims.string> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes; slot_reads = slot_reads; slot_writes = slot_writes} -> begin
     content_writes
     end))


let __proj__Mkfootprint__item__unknown_parent_writes : footprint  ->  Prims.list<Prims.string> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes; slot_reads = slot_reads; slot_writes = slot_writes} -> begin
     unknown_parent_writes
     end))


let __proj__Mkfootprint__item__slot_reads : footprint  ->  Prims.list<slot> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes; slot_reads = slot_reads; slot_writes = slot_writes} -> begin
     slot_reads
     end))


let __proj__Mkfootprint__item__slot_writes : footprint  ->  Prims.list<slot> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes; slot_reads = slot_reads; slot_writes = slot_writes} -> begin
     slot_writes
     end))


let writes_structure : footprint  ->  Prims.bool = (fun ( f  :  footprint ) -> ((not ((is_empty f.structure_writes))) || (not ((is_empty f.unknown_parent_writes)))))


let rec nodes : Prims.list<slot>  ->  Prims.list<Prims.string> = (fun ( l  :  Prims.list<slot> ) -> (match (l) with
| [] -> begin
     []
     end
| ((n, uu___))::t -> begin
     (n)::(nodes t)
     end))


let slot_clash : footprint  ->  footprint  ->  Prims.list<slot> = (fun ( a  :  footprint ) ( b  :  footprint ) -> (union (ListAlgebra.inter a.slot_writes b.slot_writes) (union (ListAlgebra.inter a.slot_writes b.slot_reads) (ListAlgebra.inter a.slot_reads b.slot_writes))))


let slots_against_node : footprint  ->  footprint  ->  Prims.list<Prims.string> = (fun ( a  :  footprint ) ( b  :  footprint ) -> (union (ListAlgebra.inter (nodes a.slot_writes) (union b.content_writes (union b.reads b.structure_writes))) (ListAlgebra.inter (nodes a.slot_reads) b.content_writes)))


let independent : footprint  ->  footprint  ->  Prims.bool = (fun ( a  :  footprint ) ( b  :  footprint ) -> (((((((((disjoint a.content_writes b.content_writes) && (disjoint a.content_writes b.reads)) && (disjoint b.content_writes a.reads)) && (disjoint a.structure_writes b.structure_writes)) && (not (((not ((is_empty a.unknown_parent_writes))) && (writes_structure b))))) && (not (((not ((is_empty b.unknown_parent_writes))) && (writes_structure a))))) && (is_empty (slot_clash a b))) && (is_empty (slots_against_node a b))) && (is_empty (slots_against_node b a))))

type shape =
| ConcurrentUpdate
| InsertPositionClash
| MoveVsRemove
| SlotClash of Prims.string


let uu___is_ConcurrentUpdate : shape  ->  Prims.bool = (fun ( projectee  :  shape ) -> (match (projectee) with
| ConcurrentUpdate -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_InsertPositionClash : shape  ->  Prims.bool = (fun ( projectee  :  shape ) -> (match (projectee) with
| InsertPositionClash -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_MoveVsRemove : shape  ->  Prims.bool = (fun ( projectee  :  shape ) -> (match (projectee) with
| MoveVsRemove -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SlotClash : shape  ->  Prims.bool = (fun ( projectee  :  shape ) -> (match (projectee) with
| SlotClash (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SlotClash__item___0 : shape  ->  Prims.string = (fun ( projectee  :  shape ) -> (match (projectee) with
| SlotClash (_0) -> begin
     _0
     end))

type conflict<'op> = {left : 'op; right : 'op; address : Prims.string; shape : shape}


let __proj__Mkconflict__item__left = (fun ( projectee  :  conflict<'op> ) -> (match (projectee) with
| {left = left; right = right; address = address; shape = shape1} -> begin
     left
     end))


let __proj__Mkconflict__item__right = (fun ( projectee  :  conflict<'op> ) -> (match (projectee) with
| {left = left; right = right; address = address; shape = shape1} -> begin
     right
     end))


let __proj__Mkconflict__item__address = (fun ( projectee  :  conflict<'op> ) -> (match (projectee) with
| {left = left; right = right; address = address; shape = shape1} -> begin
     address
     end))


let __proj__Mkconflict__item__shape = (fun ( projectee  :  conflict<'op> ) -> (match (projectee) with
| {left = left; right = right; address = address; shape = shape1} -> begin
     shape1
     end))


let concurrent : footprint  ->  footprint  ->  Prims.list<Prims.string> = (fun ( fa  :  footprint ) ( fb  :  footprint ) -> (union (ListAlgebra.inter fa.content_writes fb.content_writes) (union (ListAlgebra.inter fa.content_writes fb.reads) (ListAlgebra.inter fb.content_writes fa.reads))))


let insert_clash : footprint  ->  footprint  ->  Prims.list<Prims.string> = (fun ( fa  :  footprint ) ( fb  :  footprint ) -> (ListAlgebra.inter fa.structure_writes fb.structure_writes))


let move_remove : footprint  ->  footprint  ->  Prims.list<Prims.string> = (fun ( fa  :  footprint ) ( fb  :  footprint ) -> (union ( 
if ((not ((is_empty fa.unknown_parent_writes))) && (writes_structure fb)) then begin
     fa.unknown_parent_writes
     end else begin
     []
     end) ( 
if ((not ((is_empty fb.unknown_parent_writes))) && (writes_structure fa)) then begin
     fb.unknown_parent_writes
     end else begin
     []
     end)))


let rec tag = (fun ( a  :  'op ) ( b  :  'op ) ( s  :  shape ) ( addrs  :  Prims.list<Prims.string> ) -> (match (addrs) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ({left = a; right = b; address = x; shape = s})::(tag a b s t)
     end))


let rec tag_slots = (fun ( a  :  'op ) ( b  :  'op ) ( slots  :  Prims.list<slot> ) -> (match (slots) with
| [] -> begin
     []
     end
| ((n, s))::t -> begin
     ({left = a; right = b; address = n; shape = SlotClash (s)})::(tag_slots a b t)
     end))


let concurrent_or_slots : footprint  ->  footprint  ->  Prims.list<Prims.string> = (fun ( fa  :  footprint ) ( fb  :  footprint ) -> (union (concurrent fa fb) (union (slots_against_node fa fb) (slots_against_node fb fa))))


let pair_conflicts = (fun ( fp  :  'op  ->  footprint ) ( a  :  'op ) ( b  :  'op ) -> (

let fa = (fp a)
in (

let fb = (fp b)
in (

let c1 = (concurrent_or_slots fa fb)
in (

let c2 = (ListAlgebra.diff (insert_clash fa fb) c1)
in (

let c3 = (ListAlgebra.diff (ListAlgebra.diff (move_remove fa fb) c1) c2)
in (

let c4 = (slot_clash fa fb)
in (ListAlgebra.app (tag a b ConcurrentUpdate c1) (ListAlgebra.app (tag a b InsertPositionClash c2) (ListAlgebra.app (tag a b MoveVsRemove c3) (tag_slots a b c4)))))))))))


let rec conflicts_with = (fun ( fp  :  'op  ->  footprint ) ( a  :  'op ) ( db  :  Prims.list<'op> ) -> (match (db) with
| [] -> begin
     []
     end
| (b)::t -> begin
     (ListAlgebra.app (pair_conflicts fp a b) (conflicts_with fp a t))
     end))


let rec conflicts = (fun ( fp  :  'op  ->  footprint ) ( da  :  Prims.list<'op> ) ( db  :  Prims.list<'op> ) -> (match (da) with
| [] -> begin
     []
     end
| (a)::t -> begin
     (ListAlgebra.app (conflicts_with fp a db) (conflicts fp t db))
     end))

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


let bind = (fun ( r  :  outcome<'state, 'rej> ) ( f  :  'state  ->  outcome<'state, 'rej> ) -> (match (r) with
| Ok (s) -> begin
     (f s)
     end
| Error (e) -> begin
     Error (e)
     end))


let rec lane_conflicts = (fun ( fp  :  'op  ->  footprint ) ( d  :  Prims.list<'op> ) ( rest  :  Prims.list<Prims.list<'op>> ) -> (match (rest) with
| [] -> begin
     []
     end
| (e)::t -> begin
     (ListAlgebra.app (conflicts fp d e) (lane_conflicts fp d t))
     end))


let rec all_conflicts = (fun ( fp  :  'op  ->  footprint ) ( ds  :  Prims.list<Prims.list<'op>> ) -> (match (ds) with
| [] -> begin
     []
     end
| (d)::rest -> begin
     (ListAlgebra.app (lane_conflicts fp d rest) (all_conflicts fp rest))
     end))


let reconcile_many = (fun ( fp  :  'op  ->  footprint ) ( ds  :  Prims.list<Prims.list<'op>> ) -> (match ((all_conflicts fp ds)) with
| [] -> begin
     Ok ((concat ds))
     end
| cs -> begin
     Error (cs)
     end))


let rec replay = (fun ( apply  :  'op  ->  'state  ->  outcome<'state, 'rej> ) ( script  :  Prims.list<'op> ) ( s  :  'state ) -> (match (script) with
| [] -> begin
     Ok (s)
     end
| (o)::t -> begin
     (bind (apply o s) (replay apply t))
     end))

type lane_outcome<'op, 'state, 'rej> =
| LaneFolded of 'state
| LaneHalted of Prims.list<conflict<'op>>
| LaneRejected of 'rej
| LanesRejected of Prims.list<Prims.list<'op>>


let uu___is_LaneFolded = (fun ( projectee  :  lane_outcome<'op, 'state, 'rej> ) -> (match (projectee) with
| LaneFolded (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LaneFolded__item___0 = (fun ( projectee  :  lane_outcome<'op, 'state, 'rej> ) -> (match (projectee) with
| LaneFolded (_0) -> begin
     _0
     end))


let uu___is_LaneHalted = (fun ( projectee  :  lane_outcome<'op, 'state, 'rej> ) -> (match (projectee) with
| LaneHalted (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LaneHalted__item___0 = (fun ( projectee  :  lane_outcome<'op, 'state, 'rej> ) -> (match (projectee) with
| LaneHalted (_0) -> begin
     _0
     end))


let uu___is_LaneRejected = (fun ( projectee  :  lane_outcome<'op, 'state, 'rej> ) -> (match (projectee) with
| LaneRejected (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LaneRejected__item___0 = (fun ( projectee  :  lane_outcome<'op, 'state, 'rej> ) -> (match (projectee) with
| LaneRejected (_0) -> begin
     _0
     end))


let uu___is_LanesRejected = (fun ( projectee  :  lane_outcome<'op, 'state, 'rej> ) -> (match (projectee) with
| LanesRejected (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LanesRejected__item___0 = (fun ( projectee  :  lane_outcome<'op, 'state, 'rej> ) -> (match (projectee) with
| LanesRejected (_0) -> begin
     _0
     end))


let rec rejecting = (fun ( apply  :  'op  ->  'state  ->  outcome<'state, 'rej> ) ( s0  :  'state ) ( lanes  :  Prims.list<Prims.list<'op>> ) -> (match (lanes) with
| [] -> begin
     []
     end
| (l)::t -> begin
      
if (match ((replay apply l s0)) with
| Ok (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     (rejecting apply s0 t)
     end else begin
     (l)::(rejecting apply s0 t)
     end
     end))


let fold_once = (fun ( apply  :  'op  ->  'state  ->  outcome<'state, 'rej> ) ( fp  :  'op  ->  footprint ) ( s0  :  'state ) ( lanes  :  Prims.list<Prims.list<'op>> ) -> (match ((reconcile_many fp lanes)) with
| Error (cs) -> begin
     LaneHalted (cs)
     end
| Ok (script) -> begin
     (match ((rejecting apply s0 lanes)) with
| [] -> begin
     (match ((replay apply script s0)) with
| Ok (s) -> begin
     LaneFolded (s)
     end
| Error (r) -> begin
     LaneRejected (r)
     end)
     end
| rs -> begin
     LanesRejected (rs)
     end)
     end))


let ueq = (fun ( c  :  conflict<'op> ) ( d  :  conflict<'op> ) -> (((Prims.op_Equals c.address d.address) && (Prims.op_Equals c.shape d.shape)) && (((Prims.op_Equals c.left d.left) && (Prims.op_Equals c.right d.right)) || ((Prims.op_Equals c.left d.right) && (Prims.op_Equals c.right d.left)))))


let rec mem_u = (fun ( c  :  conflict<'op> ) ( cs  :  Prims.list<conflict<'op>> ) -> (match (cs) with
| [] -> begin
     false
     end
| (d)::t -> begin
     ((ueq c d) || (mem_u c t))
     end))


let rec insert_by = (fun ( lt  :  'k  ->  'k  ->  Prims.bool ) ( key  :  'a  ->  'k ) ( x  :  'a ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (x)::[]
     end
| (y)::t -> begin
      
if (lt (key y) (key x)) then begin
     (y)::(insert_by lt key x t)
     end else begin
     (x)::(y)::t
     end
     end))


let rec sort_by = (fun ( lt  :  'k  ->  'k  ->  Prims.bool ) ( key  :  'a  ->  'k ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (insert_by lt key x (sort_by lt key t))
     end))


let rec sorted_by = (fun ( lt  :  'k  ->  'k  ->  Prims.bool ) ( key  :  'a  ->  'k ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     (match (t) with
| [] -> begin
     true
     end
| (y)::uu___ -> begin
     ((not ((lt (key y) (key x)))) && (sorted_by lt key t))
     end)
     end))


let rec keys_of = (fun ( key  :  'a  ->  'k ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((key x))::(keys_of key t)
     end))


let ident = (fun ( x  :  'a ) -> x)


let rec dedup = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (ListAlgebra.mem x t) then begin
     (dedup t)
     end else begin
     (x)::(dedup t)
     end
     end))


let canon = (fun ( lt  :  'a  ->  'a  ->  Prims.bool ) ( l  :  Prims.list<'a> ) -> (sort_by lt ident (dedup l)))


let slot_lt : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  slot  ->  slot  ->  Prims.bool = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( p  :  slot ) ( q  :  slot ) -> (

let uu___ = p
in (match (uu___) with
| (n1, s1) -> begin
     (

let uu___1 = q
in (match (uu___1) with
| (n2, s2) -> begin
     ((lt n1 n2) || ((Prims.op_Equals n1 n2) && (lt s1 s2)))
     end))
     end)))


let pair_conflicts_emitted = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( fp  :  'op  ->  footprint ) ( a  :  'op ) ( b  :  'op ) -> (

let fa = (fp a)
in (

let fb = (fp b)
in (

let c1 = (canon lt (concurrent_or_slots fa fb))
in (

let c2 = (canon lt (ListAlgebra.diff (insert_clash fa fb) c1))
in (

let c3 = (canon lt (ListAlgebra.diff (ListAlgebra.diff (move_remove fa fb) c1) c2))
in (

let c4 = (canon (slot_lt lt) (slot_clash fa fb))
in (ListAlgebra.app (tag a b ConcurrentUpdate c1) (ListAlgebra.app (tag a b InsertPositionClash c2) (ListAlgebra.app (tag a b MoveVsRemove c3) (tag_slots a b c4)))))))))))


let rec conflicts_with_emitted = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( fp  :  'op  ->  footprint ) ( a  :  'op ) ( db  :  Prims.list<'op> ) -> (match (db) with
| [] -> begin
     []
     end
| (b)::t -> begin
     (ListAlgebra.app (pair_conflicts_emitted lt fp a b) (conflicts_with_emitted lt fp a t))
     end))


let rec conflicts_emitted = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( fp  :  'op  ->  footprint ) ( da  :  Prims.list<'op> ) ( db  :  Prims.list<'op> ) -> (match (da) with
| [] -> begin
     []
     end
| (a)::t -> begin
     (ListAlgebra.app (conflicts_with_emitted lt fp a db) (conflicts_emitted lt fp t db))
     end))


let rec lane_conflicts_emitted = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( fp  :  'op  ->  footprint ) ( d  :  Prims.list<'op> ) ( rest  :  Prims.list<Prims.list<'op>> ) -> (match (rest) with
| [] -> begin
     []
     end
| (e)::t -> begin
     (ListAlgebra.app (conflicts_emitted lt fp d e) (lane_conflicts_emitted lt fp d t))
     end))


let rec all_conflicts_emitted = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( fp  :  'op  ->  footprint ) ( ds  :  Prims.list<Prims.list<'op>> ) -> (match (ds) with
| [] -> begin
     []
     end
| (d)::rest -> begin
     (ListAlgebra.app (lane_conflicts_emitted lt fp d rest) (all_conflicts_emitted lt fp rest))
     end))


let reconcile_many_emitted = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( fp  :  'op  ->  footprint ) ( ds  :  Prims.list<Prims.list<'op>> ) -> (match ((all_conflicts_emitted lt fp ds)) with
| [] -> begin
     Ok ((concat ds))
     end
| cs -> begin
     Error (cs)
     end))


let twin_ord : Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( x  :  Prims.string ) ( y  :  Prims.string ) -> (((Prims.op_Equals x "a") && (not ((Prims.op_Equals y "a")))) || ((Prims.op_Equals x "b") && (Prims.op_Equals y "c"))))


let twin_cw : Prims.string  ->  footprint = (fun ( uu___  :  Prims.string ) -> {reads = ("n")::[]; structure_writes = []; content_writes = ("n")::[]; unknown_parent_writes = []; slot_reads = []; slot_writes = []})


let twins : Prims.list<Twin.twin> = ({Twin.tname = "canon-enumerates-the-set"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (canon twin_ord (("c")::("a")::("c")::("b")::[])) (("a")::("b")::("c")::[])))})::({Twin.tname = "emitted-report-lists-an-address-once"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (ListAlgebra.len (pair_conflicts twin_cw "p" "q")) (Prims.parse_int "3")) && (Prims.op_Equals (pair_conflicts_emitted twin_ord twin_cw "p" "q") (({left = "p"; right = "q"; address = "n"; shape = ConcurrentUpdate})::[]))))})::({Twin.tname = "independent-disjoint-footprints"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (independent {reads = ("p")::[]; structure_writes = ("p")::[]; content_writes = ("n")::[]; unknown_parent_writes = []; slot_reads = []; slot_writes = []} {reads = ("q")::[]; structure_writes = ("q")::[]; content_writes = ("m")::[]; unknown_parent_writes = []; slot_reads = []; slot_writes = []}) true))})::({Twin.tname = "dependent-shared-structure-write"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (independent {reads = []; structure_writes = ("p")::[]; content_writes = []; unknown_parent_writes = []; slot_reads = []; slot_writes = []} {reads = []; structure_writes = ("p")::[]; content_writes = []; unknown_parent_writes = []; slot_reads = []; slot_writes = []}) false))})::[]




