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


let rec rev = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (app (rev t) ((x)::[]))
     end))


let rec distinct = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((not ((mem x t))) && (distinct t))
     end))

type node<'op> = {nid : Prims.string; nparents : Prims.list<Prims.string>; nop : 'op}


let __proj__Mknode__item__nid = (fun ( projectee  :  node<'op> ) -> (match (projectee) with
| {nid = nid; nparents = nparents; nop = nop} -> begin
     nid
     end))


let __proj__Mknode__item__nparents = (fun ( projectee  :  node<'op> ) -> (match (projectee) with
| {nid = nid; nparents = nparents; nop = nop} -> begin
     nparents
     end))


let __proj__Mknode__item__nop = (fun ( projectee  :  node<'op> ) -> (match (projectee) with
| {nid = nid; nparents = nparents; nop = nop} -> begin
     nop
     end))

type dag<'op> = {nodes : Prims.list<node<'op>>}


let __proj__Mkdag__item__nodes = (fun ( projectee  :  dag<'op> ) -> (match (projectee) with
| {nodes = nodes} -> begin
     nodes
     end))

type found<'a> =
| Missing
| Found of 'a


let uu___is_Missing = (fun ( projectee  :  found<'a> ) -> (match (projectee) with
| Missing -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Found = (fun ( projectee  :  found<'a> ) -> (match (projectee) with
| Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Found__item___0 = (fun ( projectee  :  found<'a> ) -> (match (projectee) with
| Found (_0) -> begin
     _0
     end))


let rec lookup = (fun ( ns  :  Prims.list<node<'op>> ) ( id  :  Prims.string ) -> (match (ns) with
| [] -> begin
     Missing
     end
| (n)::t -> begin
      
if (Prims.op_Equals n.nid id) then begin
     Found (n)
     end else begin
     (lookup t id)
     end
     end))


let rec ids_of = (fun ( ns  :  Prims.list<node<'op>> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     (n.nid)::(ids_of t)
     end))


let rec ops_of = (fun ( ns  :  Prims.list<node<'op>> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     (n.nop)::(ops_of t)
     end))


let distinct_ids = (fun ( d  :  dag<'op> ) -> (distinct (ids_of d.nodes)))


let rec chain_head_rev = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( actor  :  Prims.string ) ( q  :  Prims.string ) ( rl  :  Prims.list<'op> ) -> (match (rl) with
| [] -> begin
     q
     end
| (o)::t -> begin
     (mint (chain_head_rev mint actor q t) actor o)
     end))


let rec chain_rev = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( actor  :  Prims.string ) ( q  :  Prims.string ) ( rl  :  Prims.list<'op> ) -> (match (rl) with
| [] -> begin
     []
     end
| (o)::t -> begin
     (

let p = (chain_head_rev mint actor q t)
in ({nid = (mint p actor o); nparents = (p)::[]; nop = o})::(chain_rev mint actor q t))
     end))


let lane_nodes = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( actor  :  Prims.string ) ( q  :  Prims.string ) ( l  :  Prims.list<'op> ) -> (rev (chain_rev mint actor q (rev l))))


let lane_head = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( actor  :  Prims.string ) ( q  :  Prims.string ) ( l  :  Prims.list<'op> ) -> (chain_head_rev mint actor q (rev l)))

type lane<'op> = {lactor : Prims.string; lops : Prims.list<'op>}


let __proj__Mklane__item__lactor = (fun ( projectee  :  lane<'op> ) -> (match (projectee) with
| {lactor = lactor; lops = lops} -> begin
     lactor
     end))


let __proj__Mklane__item__lops = (fun ( projectee  :  lane<'op> ) -> (match (projectee) with
| {lactor = lactor; lops = lops} -> begin
     lops
     end))


let rec lanes_nodes = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( q  :  Prims.string ) ( lanes  :  Prims.list<lane<'op>> ) -> (match (lanes) with
| [] -> begin
     []
     end
| (ln)::t -> begin
     (app (lane_nodes mint ln.lactor q ln.lops) (lanes_nodes mint q t))
     end))


let rec lane_heads = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( q  :  Prims.string ) ( lanes  :  Prims.list<lane<'op>> ) -> (match (lanes) with
| [] -> begin
     []
     end
| (ln)::t -> begin
     ((lane_head mint ln.lactor q ln.lops))::(lane_heads mint q t)
     end))


let rec lane_ops = (fun ( lanes  :  Prims.list<lane<'op>> ) -> (match (lanes) with
| [] -> begin
     []
     end
| (ln)::t -> begin
     (ln.lops)::(lane_ops t)
     end))


let build_dag = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( base_id  :  Prims.string ) ( base_op  :  'op ) ( lanes  :  Prims.list<lane<'op>> ) -> {nodes = ({nid = base_id; nparents = []; nop = base_op})::(lanes_nodes mint base_id lanes)})


let rec ancestors_of = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( id  :  Prims.string ) -> (match (fuel) with
| [] -> begin
     []
     end
| (uu___)::fuel' -> begin
     (match ((lookup d.nodes id)) with
| Missing -> begin
     []
     end
| Found (n) -> begin
     (id)::(ancestors_all d fuel' n.nparents)
     end)
     end))
and ancestors_all = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( ids  :  Prims.list<Prims.string> ) -> (match (ids) with
| [] -> begin
     []
     end
| (p)::t -> begin
     (app (ancestors_of d fuel p) (ancestors_all d fuel t))
     end))


let rec covers = (fun ( fuel  :  Prims.list<'a> ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     (match (fuel) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| (uu___)::t -> begin
     (match (fuel) with
| [] -> begin
     false
     end
| (uu___1)::f -> begin
     (covers f t)
     end)
     end))


let rec drop_by = (fun ( fuel  :  Prims.list<'a> ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     fuel
     end
| (uu___)::t -> begin
     (match (fuel) with
| [] -> begin
     []
     end
| (uu___1)::f -> begin
     (drop_by f t)
     end)
     end))


let topo_of = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( head  :  Prims.string ) -> (rev (ancestors_of d fuel head)))


let rec nodes_for = (fun ( d  :  dag<'op> ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (id)::t -> begin
     (match ((lookup d.nodes id)) with
| Missing -> begin
     (nodes_for d t)
     end
| Found (n) -> begin
     (n)::(nodes_for d t)
     end)
     end))


let between = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( head  :  Prims.string ) -> (nodes_for d (diff (topo_of d fuel head) (ancestors_of d fuel base_id))))


let between_ops = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( head  :  Prims.string ) -> (ops_of (between d fuel base_id head)))


let rec deltas_of = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( heads  :  Prims.list<Prims.string> ) -> (match (heads) with
| [] -> begin
     []
     end
| (h)::t -> begin
     ((between_ops d fuel base_id h))::(deltas_of d fuel base_id t)
     end))


let reconcile_many_dag = (fun ( fp  :  'op  ->  footprint ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( heads  :  Prims.list<Prims.string> ) -> (reconcile_many fp (deltas_of d fuel base_id heads)))


let fold_once_dag = (fun ( apply  :  'op  ->  'state  ->  outcome<'state, 'rej> ) ( fp  :  'op  ->  footprint ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( s0  :  'state ) ( heads  :  Prims.list<Prims.string> ) -> (fold_once apply fp s0 (deltas_of d fuel base_id heads)))


let rec before : Prims.string  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( x  :  Prims.string ) ( y  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     false
     end
| (h)::t -> begin
      
if (Prims.op_Equals h x) then begin
     (mem y t)
     end else begin
      
if (Prims.op_Equals h y) then begin
     false
     end else begin
     (before x y t)
     end
     end
     end))


let rec parents_chain = (fun ( ns  :  Prims.list<node<'op>> ) ( q  :  Prims.string ) -> (match (ns) with
| [] -> begin
     true
     end
| (n)::t -> begin
     ((Prims.op_Equals n.nparents ((q)::[])) && (parents_chain t n.nid))
     end))


let rec all_before : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( ps  :  Prims.list<Prims.string> ) ( child  :  Prims.string ) ( ord  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     true
     end
| (p)::t -> begin
     ((before p child ord) && (all_before t child ord))
     end))


let rec follows_parents = (fun ( ns  :  Prims.list<node<'op>> ) ( ord  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     true
     end
| (n)::t -> begin
     ((all_before n.nparents n.nid ord) && (follows_parents t ord))
     end))


let rec follows_spine : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( root  :  Prims.string ) ( ids  :  Prims.list<Prims.string> ) ( ord  :  Prims.list<Prims.string> ) -> (match (ids) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((before root x ord) && (follows_spine x t ord))
     end))


let rec parents_in : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( ps  :  Prims.list<Prims.string> ) ( closure  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
      
if (mem p closure) then begin
     (p)::(parents_in t closure)
     end else begin
     (parents_in t closure)
     end
     end))


let rec all_emitted : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( ps  :  Prims.list<Prims.string> ) ( emitted  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     true
     end
| (p)::t -> begin
     ((mem p emitted) && (all_emitted t emitted))
     end))


let rec frontier = (fun ( rest  :  Prims.list<node<'op>> ) ( closure  :  Prims.list<Prims.string> ) ( emitted  :  Prims.list<Prims.string> ) -> (match (rest) with
| [] -> begin
     []
     end
| (n)::t -> begin
      
if (all_emitted (parents_in n.nparents closure) emitted) then begin
     (n.nid)::(frontier t closure emitted)
     end else begin
     (frontier t closure emitted)
     end
     end))


let rec remove_id = (fun ( ns  :  Prims.list<node<'op>> ) ( id  :  Prims.string ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
      
if (Prims.op_Equals n.nid id) then begin
     t
     end else begin
     (n)::(remove_id t id)
     end
     end))


let pick_head : Prims.list<Prims.string>  ->  Prims.string = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     ""
     end
| (h)::uu___ -> begin
     h
     end))


let rec kahn = (fun ( pick  :  Prims.list<Prims.string>  ->  Prims.string ) ( fuel  :  Prims.list<node<'op>> ) ( rest  :  Prims.list<node<'op>> ) ( closure  :  Prims.list<Prims.string> ) ( emitted  :  Prims.list<Prims.string> ) -> (match (fuel) with
| [] -> begin
     []
     end
| (uu___)::fuel' -> begin
     (match ((frontier rest closure emitted)) with
| [] -> begin
     []
     end
| f -> begin
     (

let id = (pick f)
in (id)::(kahn pick fuel' (remove_id rest id) closure (app emitted ((id)::[]))))
     end)
     end))


let lane_ids_distinct = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( bn  :  node<'op> ) ( ln  :  lane<'op> ) -> (distinct ((bn.nid)::(ids_of (lane_nodes mint ln.lactor bn.nid ln.lops)))))


let between_ordered = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( ord  :  Prims.list<Prims.string> ) -> (nodes_for d (diff ord (ancestors_of d fuel base_id))))


let between_ops_ordered = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( ord  :  Prims.list<Prims.string> ) -> (ops_of (between_ordered d fuel base_id ord)))


let rec deltas_of_ordered = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( ords  :  Prims.list<Prims.list<Prims.string>> ) -> (match (ords) with
| [] -> begin
     []
     end
| (o)::t -> begin
     ((between_ops_ordered d fuel base_id o))::(deltas_of_ordered d fuel base_id t)
     end))


let reconcile_many_dag_ordered = (fun ( fp  :  'op  ->  footprint ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( ords  :  Prims.list<Prims.list<Prims.string>> ) -> (reconcile_many fp (deltas_of_ordered d fuel base_id ords)))


let fold_once_dag_ordered = (fun ( apply  :  'op  ->  'state  ->  outcome<'state, 'rej> ) ( fp  :  'op  ->  footprint ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( s0  :  'state ) ( ords  :  Prims.list<Prims.list<Prims.string>> ) -> (fold_once apply fp s0 (deltas_of_ordered d fuel base_id ords)))




