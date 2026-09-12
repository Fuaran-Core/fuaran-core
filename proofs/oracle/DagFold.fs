module DagFold

let rec mem = (fun ( x  :  'a ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals x y) || (mem x t))
     end))


let rec app = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     m
     end
| (x)::t -> begin
     (x)::(app t m)
     end))


let rec concat = (fun ( ls  :  Prims.list<Prims.list<'a>> ) -> (match (ls) with
| [] -> begin
     []
     end
| (l)::t -> begin
     (app l (concat t))
     end))


let rec inter = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (match (x) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (mem h y) then begin
     (h)::(inter t y)
     end else begin
     (inter t y)
     end
     end))


let rec diff = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (match (x) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (mem h y) then begin
     (diff t y)
     end else begin
     (h)::(diff t y)
     end
     end))


let union = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (app x y))


let is_empty = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end))


let disjoint = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (is_empty (inter x y)))

type footprint = {reads : Prims.list<Prims.string>; structure_writes : Prims.list<Prims.string>; content_writes : Prims.list<Prims.string>; unknown_parent_writes : Prims.list<Prims.string>}


let __proj__Mkfootprint__item__reads : footprint  ->  Prims.list<Prims.string> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes} -> begin
     reads
     end))


let __proj__Mkfootprint__item__structure_writes : footprint  ->  Prims.list<Prims.string> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes} -> begin
     structure_writes
     end))


let __proj__Mkfootprint__item__content_writes : footprint  ->  Prims.list<Prims.string> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes} -> begin
     content_writes
     end))


let __proj__Mkfootprint__item__unknown_parent_writes : footprint  ->  Prims.list<Prims.string> = (fun ( projectee  :  footprint ) -> (match (projectee) with
| {reads = reads; structure_writes = structure_writes; content_writes = content_writes; unknown_parent_writes = unknown_parent_writes} -> begin
     unknown_parent_writes
     end))


let writes_structure : footprint  ->  Prims.bool = (fun ( f  :  footprint ) -> ((not ((is_empty f.structure_writes))) || (not ((is_empty f.unknown_parent_writes)))))


let independent : footprint  ->  footprint  ->  Prims.bool = (fun ( a  :  footprint ) ( b  :  footprint ) -> ((((((disjoint a.content_writes b.content_writes) && (disjoint a.content_writes b.reads)) && (disjoint b.content_writes a.reads)) && (disjoint a.structure_writes b.structure_writes)) && (not (((not ((is_empty a.unknown_parent_writes))) && (writes_structure b))))) && (not (((not ((is_empty b.unknown_parent_writes))) && (writes_structure a))))))

type shape =
| ConcurrentUpdate
| InsertPositionClash
| MoveVsRemove


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


let concurrent : footprint  ->  footprint  ->  Prims.list<Prims.string> = (fun ( fa  :  footprint ) ( fb  :  footprint ) -> (union (inter fa.content_writes fb.content_writes) (union (inter fa.content_writes fb.reads) (inter fb.content_writes fa.reads))))


let insert_clash : footprint  ->  footprint  ->  Prims.list<Prims.string> = (fun ( fa  :  footprint ) ( fb  :  footprint ) -> (inter fa.structure_writes fb.structure_writes))


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


let pair_conflicts = (fun ( fp  :  'op  ->  footprint ) ( a  :  'op ) ( b  :  'op ) -> (

let fa = (fp a)
in (

let fb = (fp b)
in (

let c1 = (concurrent fa fb)
in (

let c2 = (diff (insert_clash fa fb) c1)
in (

let c3 = (diff (diff (move_remove fa fb) c1) c2)
in (app (tag a b ConcurrentUpdate c1) (app (tag a b InsertPositionClash c2) (tag a b MoveVsRemove c3)))))))))


let rec conflicts_with = (fun ( fp  :  'op  ->  footprint ) ( a  :  'op ) ( db  :  Prims.list<'op> ) -> (match (db) with
| [] -> begin
     []
     end
| (b)::t -> begin
     (app (pair_conflicts fp a b) (conflicts_with fp a t))
     end))


let rec conflicts = (fun ( fp  :  'op  ->  footprint ) ( da  :  Prims.list<'op> ) ( db  :  Prims.list<'op> ) -> (match (da) with
| [] -> begin
     []
     end
| (a)::t -> begin
     (app (conflicts_with fp a db) (conflicts fp t db))
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
     (app (conflicts fp d e) (lane_conflicts fp d t))
     end))


let rec all_conflicts = (fun ( fp  :  'op  ->  footprint ) ( ds  :  Prims.list<Prims.list<'op>> ) -> (match (ds) with
| [] -> begin
     []
     end
| (d)::rest -> begin
     (app (lane_conflicts fp d rest) (all_conflicts fp rest))
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


let fold_once = (fun ( apply  :  'op  ->  'state  ->  outcome<'state, 'rej> ) ( fp  :  'op  ->  footprint ) ( s0  :  'state ) ( lanes  :  Prims.list<Prims.list<'op>> ) -> (match ((reconcile_many fp lanes)) with
| Error (cs) -> begin
     LaneHalted (cs)
     end
| Ok (script) -> begin
     (match ((replay apply script s0)) with
| Ok (s) -> begin
     LaneFolded (s)
     end
| Error (r) -> begin
     LaneRejected (r)
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




