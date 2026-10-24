module DagClosure
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


let distinct_ids = (fun ( d  :  dag<'op> ) -> (ListAlgebra.distinct (ids_of d.nodes)))


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


let lane_nodes = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( actor  :  Prims.string ) ( q  :  Prims.string ) ( l  :  Prims.list<'op> ) -> (ListAlgebra.rev (chain_rev mint actor q (ListAlgebra.rev l))))


let lane_head = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( actor  :  Prims.string ) ( q  :  Prims.string ) ( l  :  Prims.list<'op> ) -> (chain_head_rev mint actor q (ListAlgebra.rev l)))

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
     (ListAlgebra.app (lane_nodes mint ln.lactor q ln.lops) (lanes_nodes mint q t))
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
     (ListAlgebra.app (ancestors_of d fuel p) (ancestors_all d fuel t))
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


let topo_of = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( head  :  Prims.string ) -> (ListAlgebra.rev (ancestors_of d fuel head)))


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


let between = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( head  :  Prims.string ) -> (nodes_for d (ListAlgebra.diff (topo_of d fuel head) (ancestors_of d fuel base_id))))


let between_ops = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( head  :  Prims.string ) -> (ops_of (between d fuel base_id head)))


let rec deltas_of = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( heads  :  Prims.list<Prims.string> ) -> (match (heads) with
| [] -> begin
     []
     end
| (h)::t -> begin
     ((between_ops d fuel base_id h))::(deltas_of d fuel base_id t)
     end))


let reconcile_many_dag = (fun ( fp  :  'op  ->  DagFold.footprint ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( heads  :  Prims.list<Prims.string> ) -> (DagFold.reconcile_many fp (deltas_of d fuel base_id heads)))


let fold_once_dag = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( fp  :  'op  ->  DagFold.footprint ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( s0  :  'state ) ( heads  :  Prims.list<Prims.string> ) -> (DagFold.fold_once apply fp s0 (deltas_of d fuel base_id heads)))


let rec before : Prims.string  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( x  :  Prims.string ) ( y  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     false
     end
| (h)::t -> begin
      
if (Prims.op_Equals h x) then begin
     (ListAlgebra.mem y t)
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
      
if (ListAlgebra.mem p closure) then begin
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
     ((ListAlgebra.mem p emitted) && (all_emitted t emitted))
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
in (id)::(kahn pick fuel' (remove_id rest id) closure (ListAlgebra.app emitted ((id)::[]))))
     end)
     end))


let rec pick_min : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  Prims.list<Prims.string>  ->  Prims.string = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     ""
     end
| (x)::[] -> begin
     x
     end
| (x)::t -> begin
     (

let m = (pick_min lt t)
in  
if (lt x m) then begin
     x
     end else begin
     m
     end)
     end))


let rec remove_first : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( e  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (Prims.op_Equals x e) then begin
     t
     end else begin
     (x)::(remove_first e t)
     end
     end))


let rec all_before_or_emitted : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( ps  :  Prims.list<Prims.string> ) ( child  :  Prims.string ) ( emitted  :  Prims.list<Prims.string> ) ( ord  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     true
     end
| (p)::t -> begin
     (((ListAlgebra.mem p emitted) || (before p child ord)) && (all_before_or_emitted t child emitted ord))
     end))


let rec follows_parents_or_emitted = (fun ( ns  :  Prims.list<node<'op>> ) ( closure  :  Prims.list<Prims.string> ) ( emitted  :  Prims.list<Prims.string> ) ( ord  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     true
     end
| (n)::t -> begin
     ((all_before_or_emitted (parents_in n.nparents closure) n.nid emitted ord) && (follows_parents_or_emitted t closure emitted ord))
     end))


let follows_parents_in = (fun ( ns  :  Prims.list<node<'op>> ) ( closure  :  Prims.list<Prims.string> ) ( ord  :  Prims.list<Prims.string> ) -> (follows_parents_or_emitted ns closure [] ord))


let rec placed_follow_parents = (fun ( ns  :  Prims.list<node<'op>> ) ( closure  :  Prims.list<Prims.string> ) ( emitted  :  Prims.list<Prims.string> ) ( ord  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     true
     end
| (n)::t -> begin
     (( 
if (ListAlgebra.mem n.nid ord) then begin
     (all_before_or_emitted (parents_in n.nparents closure) n.nid emitted ord)
     end else begin
     true
     end) && (placed_follow_parents t closure emitted ord))
     end))


let rec sub_ids : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) ( m  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((ListAlgebra.mem x m) && (sub_ids t m))
     end))


let same_ids : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) ( m  :  Prims.list<Prims.string> ) -> ((sub_ids l m) && (sub_ids m l)))


let is_topo_enum = (fun ( ns  :  Prims.list<node<'op>> ) ( ord  :  Prims.list<Prims.string> ) -> (((ListAlgebra.distinct ord) && (same_ids ord (ids_of ns))) && (follows_parents_in ns (ids_of ns) ord)))


let rec first_outside : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  found<Prims.string> = (fun ( ps  :  Prims.list<Prims.string> ) ( closure  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     Missing
     end
| (p)::t -> begin
      
if (ListAlgebra.mem p closure) then begin
     (first_outside t closure)
     end else begin
     Found (p)
     end
     end))


let rec dangling_ids = (fun ( ns  :  Prims.list<node<'op>> ) ( closure  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     (match ((first_outside n.nparents closure)) with
| Missing -> begin
     (dangling_ids t closure)
     end
| Found (uu___) -> begin
     (n.nid)::(dangling_ids t closure)
     end)
     end))

type dangling_policy =
| IgnoreDangling
| RefuseDangling


let uu___is_IgnoreDangling : dangling_policy  ->  Prims.bool = (fun ( projectee  :  dangling_policy ) -> (match (projectee) with
| IgnoreDangling -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_RefuseDangling : dangling_policy  ->  Prims.bool = (fun ( projectee  :  dangling_policy ) -> (match (projectee) with
| RefuseDangling -> begin
     true
     end
| uu___ -> begin
     false
     end))

type drain_result =
| Drained of Prims.list<Prims.string>
| Refused of Prims.string


let uu___is_Drained : drain_result  ->  Prims.bool = (fun ( projectee  :  drain_result ) -> (match (projectee) with
| Drained (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Drained__item___0 : drain_result  ->  Prims.list<Prims.string> = (fun ( projectee  :  drain_result ) -> (match (projectee) with
| Drained (_0) -> begin
     _0
     end))


let uu___is_Refused : drain_result  ->  Prims.bool = (fun ( projectee  :  drain_result ) -> (match (projectee) with
| Refused (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Refused__item___0 : drain_result  ->  Prims.string = (fun ( projectee  :  drain_result ) -> (match (projectee) with
| Refused (_0) -> begin
     _0
     end))


let drain_order = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( fuel  :  Prims.list<node<'op>> ) ( ns  :  Prims.list<node<'op>> ) -> (kahn (pick_min lt) fuel ns (ids_of ns) []))


let drain = (fun ( policy  :  dangling_policy ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( fuel  :  Prims.list<node<'op>> ) ( ns  :  Prims.list<node<'op>> ) -> (match (policy) with
| IgnoreDangling -> begin
     Drained ((drain_order lt fuel ns))
     end
| RefuseDangling -> begin
     (match ((dangling_ids ns (ids_of ns))) with
| [] -> begin
     Drained ((drain_order lt fuel ns))
     end
| ds -> begin
     Refused ((pick_min lt ds))
     end)
     end))


let lane_ids_distinct = (fun ( mint  :  Prims.string  ->  Prims.string  ->  'op  ->  Prims.string ) ( bn  :  node<'op> ) ( ln  :  lane<'op> ) -> (ListAlgebra.distinct ((bn.nid)::(ids_of (lane_nodes mint ln.lactor bn.nid ln.lops)))))


let between_ordered = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( ord  :  Prims.list<Prims.string> ) -> (nodes_for d (ListAlgebra.diff ord (ancestors_of d fuel base_id))))


let between_ops_ordered = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( ord  :  Prims.list<Prims.string> ) -> (ops_of (between_ordered d fuel base_id ord)))


let rec deltas_of_ordered = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( ords  :  Prims.list<Prims.list<Prims.string>> ) -> (match (ords) with
| [] -> begin
     []
     end
| (o)::t -> begin
     ((between_ops_ordered d fuel base_id o))::(deltas_of_ordered d fuel base_id t)
     end))


let reconcile_many_dag_ordered = (fun ( fp  :  'op  ->  DagFold.footprint ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( ords  :  Prims.list<Prims.list<Prims.string>> ) -> (DagFold.reconcile_many fp (deltas_of_ordered d fuel base_id ords)))


let fold_once_dag_ordered = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( fp  :  'op  ->  DagFold.footprint ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( s0  :  'state ) ( ords  :  Prims.list<Prims.list<Prims.string>> ) -> (DagFold.fold_once apply fp s0 (deltas_of_ordered d fuel base_id ords)))


let by_key = (fun ( tbl  :  Prims.list<node<'op>> ) ( key  :  node<'op>  ->  'k ) ( ltk  :  'k  ->  'k  ->  Prims.bool ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( a  :  Prims.string ) ( b  :  Prims.string ) -> (match ((lookup tbl a)) with
| Found (na) -> begin
     (match ((lookup tbl b)) with
| Found (nb) -> begin
     ((ltk (key na) (key nb)) || ((Prims.op_Equals (key na) (key nb)) && (lt a b)))
     end
| Missing -> begin
     true
     end)
     end
| Missing -> begin
     (match ((lookup tbl b)) with
| Found (uu___) -> begin
     false
     end
| Missing -> begin
     (lt a b)
     end)
     end))


let rec walk_ok = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( id  :  Prims.string ) -> (match (fuel) with
| [] -> begin
     (match ((lookup d.nodes id)) with
| Missing -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| (uu___)::fuel' -> begin
     (match ((lookup d.nodes id)) with
| Missing -> begin
     true
     end
| Found (n) -> begin
     (walk_all_ok d fuel' n.nparents)
     end)
     end))
and walk_all_ok = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( ids  :  Prims.list<Prims.string> ) -> (match (ids) with
| [] -> begin
     true
     end
| (p)::t -> begin
     ((walk_ok d fuel p) && (walk_all_ok d fuel t))
     end))


let rec at_least = (fun ( big  :  Prims.list<'a> ) ( small  :  Prims.list<'a> ) -> (match (small) with
| [] -> begin
     true
     end
| (uu___)::s -> begin
     (match (big) with
| [] -> begin
     false
     end
| (uu___1)::b -> begin
     (at_least b s)
     end)
     end))


let rec closure_nodes = (fun ( ns  :  Prims.list<node<'op>> ) ( closure  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
      
if (ListAlgebra.mem n.nid closure) then begin
     (n)::(closure_nodes t closure)
     end else begin
     (closure_nodes t closure)
     end
     end))


let topo_drain = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( head  :  Prims.string ) -> (drain_order lt kfuel (closure_nodes d.nodes (ancestors_of d fuel head))))


let between_drained = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( head  :  Prims.string ) -> (between_ordered d fuel base_id (topo_drain lt kfuel d fuel head)))


let between_ops_drained = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( head  :  Prims.string ) -> (ops_of (between_drained lt kfuel d fuel base_id head)))


let head_drains = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( kfuel  :  Prims.list<node<'op>> ) ( head  :  Prims.string ) ( w  :  Prims.list<Prims.string> ) -> (

let cn = (closure_nodes d.nodes (ancestors_of d fuel head))
in ((covers kfuel (ids_of cn)) && (is_topo_enum cn w))))


let rec deltas_of_drained = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( heads  :  Prims.list<Prims.string> ) -> (match (heads) with
| [] -> begin
     []
     end
| (h)::t -> begin
     ((between_ops_drained lt kfuel d fuel base_id h))::(deltas_of_drained lt kfuel d fuel base_id t)
     end))


let reconcile_many_drained = (fun ( fp  :  'op  ->  DagFold.footprint ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( heads  :  Prims.list<Prims.string> ) -> (DagFold.reconcile_many fp (deltas_of_drained lt kfuel d fuel base_id heads)))


let fold_once_drained = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( fp  :  'op  ->  DagFold.footprint ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( base_id  :  Prims.string ) ( s0  :  'state ) ( heads  :  Prims.list<Prims.string> ) -> (DagFold.fold_once apply fp s0 (deltas_of_drained lt kfuel d fuel base_id heads)))


let rec dedup : Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
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


let rec shorter = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'b> ) -> (match (m) with
| [] -> begin
     false
     end
| (uu___)::mt -> begin
     (match (l) with
| [] -> begin
     true
     end
| (uu___1)::lt' -> begin
     (shorter lt' mt)
     end)
     end))


let closure_set = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( id  :  Prims.string ) -> (dedup (ancestors_of d fuel id)))


let deeper = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( x  :  Prims.string ) ( y  :  Prims.string ) -> (

let cx = (closure_set d fuel x)
in (

let cy = (closure_set d fuel y)
in  
if (shorter cy cx) then begin
     true
     end else begin
      
if (shorter cx cy) then begin
     false
     end else begin
     (lt y x)
     end
     end)))


let rec max_by : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.string = (fun ( better  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( best  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     best
     end
| (x)::t -> begin
      
if (better x best) then begin
     (max_by better x t)
     end else begin
     (max_by better best t)
     end
     end))


let rec owners : Prims.string  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.nat = (fun ( x  :  Prims.string ) ( cs  :  Prims.list<Prims.list<Prims.string>> ) -> (match (cs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (c)::t -> begin
     (( 
if (ListAlgebra.mem x c) then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "0")
     end) + (owners x t))
     end))


let shared_pred : Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.string  ->  Prims.bool = (fun ( base_c  :  Prims.list<Prims.string> ) ( cs  :  Prims.list<Prims.list<Prims.string>> ) ( x  :  Prims.string ) -> ((not ((ListAlgebra.mem x base_c))) && ((owners x cs) >= (Prims.parse_int "2"))))


let shared_ids : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.string> = (fun ( order  :  Prims.list<Prims.string> ) ( base_c  :  Prims.list<Prims.string> ) ( cs  :  Prims.list<Prims.list<Prims.string>> ) -> (DagFold.keep (shared_pred base_c cs) order))


let excl_pred : Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( base_c  :  Prims.list<Prims.string> ) ( cs_all  :  Prims.list<Prims.list<Prims.string>> ) ( c  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (((not ((ListAlgebra.mem x base_c))) && (ListAlgebra.mem x c)) && (Prims.op_Equals (owners x cs_all) (Prims.parse_int "1"))))


let excl_ids : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( order  :  Prims.list<Prims.string> ) ( base_c  :  Prims.list<Prims.string> ) ( cs_all  :  Prims.list<Prims.list<Prims.string>> ) ( c  :  Prims.list<Prims.string> ) -> (DagFold.keep (excl_pred base_c cs_all c) order))


let rec excl_lanes : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.list<Prims.string>> = (fun ( order  :  Prims.list<Prims.string> ) ( base_c  :  Prims.list<Prims.string> ) ( cs_all  :  Prims.list<Prims.list<Prims.string>> ) ( cs  :  Prims.list<Prims.list<Prims.string>> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     ((excl_ids order base_c cs_all c))::(excl_lanes order base_c cs_all t)
     end))


let reconcile_ids : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.string> = (fun ( order  :  Prims.list<Prims.string> ) ( base_c  :  Prims.list<Prims.string> ) ( cs  :  Prims.list<Prims.list<Prims.string>> ) -> (ListAlgebra.app (shared_ids order base_c cs) (DagFold.concat (excl_lanes order base_c cs cs))))


let rec ops_by = (fun ( opof  :  Prims.string  ->  'op ) ( ids  :  Prims.list<Prims.string> ) -> (match (ids) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((opof x))::(ops_by opof t)
     end))


let rec lanes_by = (fun ( opof  :  Prims.string  ->  'op ) ( ls  :  Prims.list<Prims.list<Prims.string>> ) -> (match (ls) with
| [] -> begin
     []
     end
| (l)::t -> begin
     ((ops_by opof l))::(lanes_by opof t)
     end))

type replay_fault<'rej> =
| RUnknownHead of Prims.string
| RCyclicHistory of Prims.string
| RRejected of Prims.string * 'rej


let uu___is_RUnknownHead = (fun ( projectee  :  replay_fault<'rej> ) -> (match (projectee) with
| RUnknownHead (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RUnknownHead__item___0 = (fun ( projectee  :  replay_fault<'rej> ) -> (match (projectee) with
| RUnknownHead (_0) -> begin
     _0
     end))


let uu___is_RCyclicHistory = (fun ( projectee  :  replay_fault<'rej> ) -> (match (projectee) with
| RCyclicHistory (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RCyclicHistory__item___0 = (fun ( projectee  :  replay_fault<'rej> ) -> (match (projectee) with
| RCyclicHistory (_0) -> begin
     _0
     end))


let uu___is_RRejected = (fun ( projectee  :  replay_fault<'rej> ) -> (match (projectee) with
| RRejected (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RRejected__item___0 = (fun ( projectee  :  replay_fault<'rej> ) -> (match (projectee) with
| RRejected (_0, _1) -> begin
     _0
     end))


let __proj__RRejected__item___1 = (fun ( projectee  :  replay_fault<'rej> ) -> (match (projectee) with
| RRejected (_0, _1) -> begin
     _1
     end))


let rec replay_ids = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( ns  :  Prims.list<node<'op>> ) ( ids  :  Prims.list<Prims.string> ) ( s  :  'state ) -> (match (ids) with
| [] -> begin
     DagFold.Ok (s)
     end
| (id)::t -> begin
     (match ((lookup ns id)) with
| Missing -> begin
     (replay_ids apply ns t s)
     end
| Found (n) -> begin
     (match ((apply n.nop s)) with
| DagFold.Ok (s') -> begin
     (replay_ids apply ns t s')
     end
| DagFold.Error (e) -> begin
     DagFold.Error (RRejected (id, e))
     end)
     end)
     end))


let replay_to = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( s0  :  'state ) ( head  :  Prims.string ) -> (match ((lookup d.nodes head)) with
| Missing -> begin
     DagFold.Error (RUnknownHead (head))
     end
| Found (uu___) -> begin
     (

let cn = (closure_nodes d.nodes (ancestors_of d fuel head))
in (

let order = (drain_order lt kfuel cn)
in  
if (sub_ids (ids_of cn) order) then begin
     (replay_ids apply d.nodes order s0)
     end else begin
     DagFold.Error (RCyclicHistory (head))
     end))
     end))


let is_unknown_head = (fun ( r  :  DagFold.outcome<'state, replay_fault<'rej>> ) -> (match (r) with
| DagFold.Error (RUnknownHead (uu___)) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let is_cyclic = (fun ( r  :  DagFold.outcome<'state, replay_fault<'rej>> ) -> (match (r) with
| DagFold.Error (RCyclicHistory (uu___)) -> begin
     true
     end
| uu___ -> begin
     false
     end))

type append_result<'op> =
| Appended of Prims.string * dag<'op>
| Collision of Prims.string


let uu___is_Appended = (fun ( projectee  :  append_result<'op> ) -> (match (projectee) with
| Appended (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Appended__item___0 = (fun ( projectee  :  append_result<'op> ) -> (match (projectee) with
| Appended (_0, _1) -> begin
     _0
     end))


let __proj__Appended__item___1 = (fun ( projectee  :  append_result<'op> ) -> (match (projectee) with
| Appended (_0, _1) -> begin
     _1
     end))


let uu___is_Collision = (fun ( projectee  :  append_result<'op> ) -> (match (projectee) with
| Collision (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Collision__item___0 = (fun ( projectee  :  append_result<'op> ) -> (match (projectee) with
| Collision (_0) -> begin
     _0
     end))


let add_node = (fun ( same  :  node<'op>  ->  node<'op>  ->  Prims.bool ) ( n  :  node<'op> ) ( d  :  dag<'op> ) -> (match ((lookup d.nodes n.nid)) with
| Found (m) -> begin
      
if (same m n) then begin
     Appended (n.nid, d)
     end else begin
     Collision (n.nid)
     end
     end
| Missing -> begin
     Appended (n.nid, {nodes = (n)::d.nodes})
     end))


type slot_store = Prims.list<(DagFold.slot * Prims.string)>


let rec lookup_slot : slot_store  ->  DagFold.slot  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( st  :  slot_store ) ( k  :  DagFold.slot ) -> (match (st) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((k', v))::t -> begin
      
if (Prims.op_Equals k k') then begin
     FStar_Pervasives_Native.Some (v)
     end else begin
     (lookup_slot t k)
     end
     end))


let write_slot : DagFold.slot  ->  Prims.string  ->  slot_store  ->  slot_store = (fun ( k  :  DagFold.slot ) ( v  :  Prims.string ) ( st  :  slot_store ) -> (((k), (v)))::st)


let slot_write_fp : DagFold.slot  ->  DagFold.footprint = (fun ( k  :  DagFold.slot ) -> {DagFold.reads = []; DagFold.structure_writes = []; DagFold.content_writes = []; DagFold.unknown_parent_writes = []; DagFold.slot_reads = (k)::[]; DagFold.slot_writes = (k)::[]})


let whole_node_write_fp : Prims.string  ->  DagFold.footprint = (fun ( n  :  Prims.string ) -> {DagFold.reads = (n)::[]; DagFold.structure_writes = []; DagFold.content_writes = (n)::[]; DagFold.unknown_parent_writes = []; DagFold.slot_reads = []; DagFold.slot_writes = []})


let common_anc = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( left  :  Prims.string ) ( right  :  Prims.string ) ( x  :  Prims.string ) -> ((ListAlgebra.mem x (ancestors_of d fuel left)) && (ListAlgebra.mem x (ancestors_of d fuel right))))


let rec map_list = (fun ( g  :  'a  ->  'b ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((g x))::(map_list g t)
     end))


let map_conflict = (fun ( g  :  'a  ->  'b ) ( c  :  DagFold.conflict<'a> ) -> {DagFold.left = (g c.left); DagFold.right = (g c.right); DagFold.address = c.address; DagFold.shape = c.shape})


let node_op = (fun ( n  :  node<'op> ) -> n.nop)


let node_fp = (fun ( fp  :  'op  ->  DagFold.footprint ) ( n  :  node<'op> ) -> (fp n.nop))


let conflicts_of_nodes = (fun ( fp  :  'op  ->  DagFold.footprint ) ( da  :  Prims.list<node<'op>> ) ( db  :  Prims.list<node<'op>> ) -> (DagFold.conflicts (node_fp fp) da db))


let rec heads_lane_sweep = (fun ( fp  :  'op  ->  DagFold.footprint ) ( d  :  Prims.list<node<'op>> ) ( rest  :  Prims.list<Prims.list<node<'op>>> ) -> (match (rest) with
| [] -> begin
     []
     end
| (e)::t -> begin
     (ListAlgebra.app (conflicts_of_nodes fp d e) (heads_lane_sweep fp d t))
     end))


let rec heads_sweep = (fun ( fp  :  'op  ->  DagFold.footprint ) ( ds  :  Prims.list<Prims.list<node<'op>>> ) -> (match (ds) with
| [] -> begin
     []
     end
| (d)::rest -> begin
     (ListAlgebra.app (heads_lane_sweep fp d rest) (heads_sweep fp rest))
     end))


let conflicts_of_heads = (fun ( fp  :  'op  ->  DagFold.footprint ) ( d  :  dag<'op> ) ( order  :  Prims.list<Prims.string> ) ( base_c  :  Prims.list<Prims.string> ) ( cs  :  Prims.list<Prims.list<Prims.string>> ) -> (heads_sweep fp (map_list (nodes_for d) (excl_lanes order base_c cs cs))))


let excl_op_lanes = (fun ( d  :  dag<'op> ) ( order  :  Prims.list<Prims.string> ) ( base_c  :  Prims.list<Prims.string> ) ( cs  :  Prims.list<Prims.list<Prims.string>> ) -> (map_list (fun ( ids  :  Prims.list<Prims.string> ) -> (ops_of (nodes_for d ids))) (excl_lanes order base_c cs cs)))

type rslot = {rid : Prims.string; rbits : Prims.list<Prims.string>}


let __proj__Mkrslot__item__rid : rslot  ->  Prims.string = (fun ( projectee  :  rslot ) -> (match (projectee) with
| {rid = rid; rbits = rbits} -> begin
     rid
     end))


let __proj__Mkrslot__item__rbits : rslot  ->  Prims.list<Prims.string> = (fun ( projectee  :  rslot ) -> (match (projectee) with
| {rid = rid; rbits = rbits} -> begin
     rbits
     end))


let rec slot_of : Prims.list<rslot>  ->  Prims.string  ->  found<Prims.list<Prims.string>> = (fun ( r  :  Prims.list<rslot> ) ( id  :  Prims.string ) -> (match (r) with
| [] -> begin
     Missing
     end
| (e)::t -> begin
      
if (Prims.op_Equals e.rid id) then begin
     Found (e.rbits)
     end else begin
     (slot_of t id)
     end
     end))


let rec parent_bits : Prims.list<rslot>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( r  :  Prims.list<rslot> ) ( ps  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
     (match ((slot_of r p)) with
| Found (b) -> begin
     (ListAlgebra.app b (parent_bits r t))
     end
| Missing -> begin
     (parent_bits r t)
     end)
     end))


let rec held_slotted = (fun ( d  :  dag<'op> ) ( r  :  Prims.list<rslot> ) ( ps  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     true
     end
| (p)::t -> begin
     (((match ((lookup d.nodes p)) with
| Missing -> begin
     true
     end
| uu___ -> begin
     false
     end) || (match ((slot_of r p)) with
| Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end)) && (held_slotted d r t))
     end))


let reach_step = (fun ( d  :  dag<'op> ) ( r  :  Prims.list<rslot> ) ( id  :  Prims.string ) -> (match ((lookup d.nodes id)) with
| Missing -> begin
     r
     end
| Found (n) -> begin
      
if (match ((slot_of r id)) with
| Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     r
     end else begin
      
if (held_slotted d r n.nparents) then begin
     ({rid = id; rbits = (id)::(parent_bits r n.nparents)})::r
     end else begin
     r
     end
     end
     end))


let rec reach_build = (fun ( d  :  dag<'op> ) ( r  :  Prims.list<rslot> ) ( ord  :  Prims.list<Prims.string> ) -> (match (ord) with
| [] -> begin
     r
     end
| (id)::t -> begin
     (reach_build d (reach_step d r id) t)
     end))


let reach_of_dag = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) -> (reach_build d [] (drain_order lt kfuel d.nodes)))


let reach_ancestors = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( r  :  Prims.list<rslot> ) ( id  :  Prims.string ) -> (match ((slot_of r id)) with
| Found (b) -> begin
     b
     end
| Missing -> begin
     (ancestors_of d fuel id)
     end))


let reach_reaches = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( r  :  Prims.list<rslot> ) ( anc  :  Prims.string ) ( dsc  :  Prims.string ) -> (match ((slot_of r dsc)) with
| Found (b) -> begin
     (match ((slot_of r anc)) with
| Found (uu___) -> begin
     (ListAlgebra.mem anc b)
     end
| Missing -> begin
     false
     end)
     end
| Missing -> begin
     (ListAlgebra.mem anc (ancestors_of d fuel dsc))
     end))


let in_set : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( c  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (ListAlgebra.mem x c))


let closure_of = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( x  :  Prims.string ) -> (closure_nodes d.nodes (ancestors_of d fuel x)))

type checkpoint<'state> = {cnode : Prims.string; cstate : 'state; cseal : Prims.string}


let __proj__Mkcheckpoint__item__cnode = (fun ( projectee  :  checkpoint<'state> ) -> (match (projectee) with
| {cnode = cnode; cstate = cstate; cseal = cseal} -> begin
     cnode
     end))


let __proj__Mkcheckpoint__item__cstate = (fun ( projectee  :  checkpoint<'state> ) -> (match (projectee) with
| {cnode = cnode; cstate = cstate; cseal = cseal} -> begin
     cstate
     end))


let __proj__Mkcheckpoint__item__cseal = (fun ( projectee  :  checkpoint<'state> ) -> (match (projectee) with
| {cnode = cnode; cstate = cstate; cseal = cseal} -> begin
     cseal
     end))

type ck_fault<'rej> =
| CUnknownNode of Prims.string
| CReplay of replay_fault<'rej>
| CUnreached of Prims.string
| CUncovered of Prims.string


let uu___is_CUnknownNode = (fun ( projectee  :  ck_fault<'rej> ) -> (match (projectee) with
| CUnknownNode (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CUnknownNode__item___0 = (fun ( projectee  :  ck_fault<'rej> ) -> (match (projectee) with
| CUnknownNode (_0) -> begin
     _0
     end))


let uu___is_CReplay = (fun ( projectee  :  ck_fault<'rej> ) -> (match (projectee) with
| CReplay (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CReplay__item___0 = (fun ( projectee  :  ck_fault<'rej> ) -> (match (projectee) with
| CReplay (_0) -> begin
     _0
     end))


let uu___is_CUnreached = (fun ( projectee  :  ck_fault<'rej> ) -> (match (projectee) with
| CUnreached (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CUnreached__item___0 = (fun ( projectee  :  ck_fault<'rej> ) -> (match (projectee) with
| CUnreached (_0) -> begin
     _0
     end))


let uu___is_CUncovered = (fun ( projectee  :  ck_fault<'rej> ) -> (match (projectee) with
| CUncovered (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CUncovered__item___0 = (fun ( projectee  :  ck_fault<'rej> ) -> (match (projectee) with
| CUncovered (_0) -> begin
     _0
     end))


let seal_at = (fun ( seal  :  Prims.string  ->  'state  ->  Prims.string ) ( c  :  Prims.string ) ( st  :  'state ) -> {cnode = c; cstate = st; cseal = (seal c st)})


let lift = (fun ( r  :  DagFold.outcome<'state, replay_fault<'rej>> ) -> (match (r) with
| DagFold.Ok (s) -> begin
     DagFold.Ok (s)
     end
| DagFold.Error (f) -> begin
     DagFold.Error (CReplay (f))
     end))


let checkpoint_at = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( seal  :  Prims.string  ->  'state  ->  Prims.string ) ( s0  :  'state ) ( c  :  Prims.string ) -> (match ((lookup d.nodes c)) with
| Missing -> begin
     DagFold.Error (CUnknownNode (c))
     end
| Found (uu___) -> begin
     (match ((replay_to apply lt kfuel d fuel s0 c)) with
| DagFold.Error (f) -> begin
     DagFold.Error (CReplay (f))
     end
| DagFold.Ok (st) -> begin
     DagFold.Ok ((seal_at seal c st))
     end)
     end))


let verify_checkpoint = (fun ( seal  :  Prims.string  ->  'state  ->  Prims.string ) ( cp  :  checkpoint<'state> ) ( d  :  dag<'op> ) -> ((match ((lookup d.nodes cp.cnode)) with
| Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (Prims.op_Equals (seal cp.cnode cp.cstate) cp.cseal)))


let rec exists_parent : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( ps  :  Prims.list<Prims.string> ) ( node1  :  Prims.string ) ( desc  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     false
     end
| (p)::t -> begin
     (((Prims.op_Equals p node1) || (ListAlgebra.mem p desc)) || (exists_parent t node1 desc))
     end))


let rec above_go = (fun ( d  :  dag<'op> ) ( node1  :  Prims.string ) ( desc  :  Prims.list<Prims.string> ) ( ids  :  Prims.list<Prims.string> ) -> (match (ids) with
| [] -> begin
     Missing
     end
| (id)::rest -> begin
     (match ((lookup d.nodes id)) with
| Missing -> begin
     Found (id)
     end
| Found (n) -> begin
      
if (exists_parent n.nparents node1 desc) then begin
     (above_go d node1 ((id)::desc) rest)
     end else begin
     Found (id)
     end
     end)
     end))


let replay_from = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( cp  :  checkpoint<'state> ) ( head  :  Prims.string ) -> (match ((lookup d.nodes cp.cnode)) with
| Missing -> begin
     DagFold.Error (CUnknownNode (cp.cnode))
     end
| Found (uu___) -> begin
     (match ((lookup d.nodes head)) with
| Missing -> begin
     DagFold.Error (CReplay (RUnknownHead (head)))
     end
| Found (uu___1) -> begin
     (

let cn = (closure_nodes d.nodes (ancestors_of d fuel head))
in (

let order = (drain_order lt kfuel cn)
in  
if (not ((sub_ids (ids_of cn) order))) then begin
     DagFold.Error (CReplay (RCyclicHistory (head)))
     end else begin
      
if (not ((ListAlgebra.mem cp.cnode order))) then begin
     DagFold.Error (CUnreached (head))
     end else begin
     (

let delta = (ListAlgebra.diff order (ancestors_of d fuel cp.cnode))
in (match ((above_go d cp.cnode [] delta)) with
| Found (id) -> begin
     DagFold.Error (CUncovered (id))
     end
| Missing -> begin
     (lift (replay_ids apply d.nodes delta cp.cstate))
     end))
     end
     end))
     end)
     end))


let out_of : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( a  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (not ((ListAlgebra.mem x a))))


let checkpoint_from = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<node<'op>> ) ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( seal  :  Prims.string  ->  'state  ->  Prims.string ) ( origin  :  checkpoint<'state> ) ( n  :  Prims.string ) ->  
if ((match ((lookup d.nodes origin.cnode)) with
| Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (match ((lookup d.nodes n)) with
| Missing -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     DagFold.Error (CUnknownNode (n))
     end else begin
     (match ((replay_from apply lt kfuel d fuel origin n)) with
| DagFold.Ok (st) -> begin
     DagFold.Ok ((seal_at seal n st))
     end
| DagFold.Error (f) -> begin
     DagFold.Error (f)
     end)
     end)


let rec remove_all = (fun ( r  :  Prims.list<node<'op>> ) ( p  :  Prims.list<Prims.string> ) -> (match (p) with
| [] -> begin
     r
     end
| (x)::t -> begin
     (remove_all (remove_id r x) t)
     end))


let rec drop_nodes = (fun ( r  :  Prims.list<node<'op>> ) ( p  :  Prims.list<Prims.string> ) -> (match (r) with
| [] -> begin
     []
     end
| (n)::t -> begin
      
if (ListAlgebra.mem n.nid p) then begin
     (drop_nodes t p)
     end else begin
     (n)::(drop_nodes t p)
     end
     end))


let pending : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( p  :  Prims.string ) ( c  :  Prims.list<Prims.string> ) ( e  :  Prims.list<Prims.string> ) -> ((ListAlgebra.mem p c) && (not ((ListAlgebra.mem p e)))))


let truncate = (fun ( d  :  dag<'op> ) ( keep  :  Prims.list<Prims.string> ) -> {nodes = (closure_nodes d.nodes keep)})


let rec trunc_ok_nodes = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( c  :  Prims.string ) ( keep  :  Prims.list<Prims.string> ) ( ns  :  Prims.list<node<'op>> ) -> (match (ns) with
| [] -> begin
     true
     end
| (n)::t -> begin
     (( 
if (ListAlgebra.mem n.nid (ancestors_of d fuel c)) then begin
     ((not ((ListAlgebra.mem n.nid keep))) || (ListAlgebra.mem n.nid (ancestors_of (truncate d keep) fuel c)))
     end else begin
     (ListAlgebra.mem n.nid keep)
     end) && (trunc_ok_nodes d fuel c keep t))
     end))


let truncation_ok = (fun ( d  :  dag<'op> ) ( fuel  :  Prims.list<node<'op>> ) ( c  :  Prims.string ) ( keep  :  Prims.list<Prims.string> ) -> ((ListAlgebra.mem c keep) && (trunc_ok_nodes d fuel c keep d.nodes)))

type lane_rejection<'op, 'rej> = {lr_head : Prims.string; lr_delta : Prims.list<'op>; lr_node : Prims.string; lr_reject : 'rej}


let __proj__Mklane_rejection__item__lr_head = (fun ( projectee  :  lane_rejection<'op, 'rej> ) -> (match (projectee) with
| {lr_head = lr_head; lr_delta = lr_delta; lr_node = lr_node; lr_reject = lr_reject} -> begin
     lr_head
     end))


let __proj__Mklane_rejection__item__lr_delta = (fun ( projectee  :  lane_rejection<'op, 'rej> ) -> (match (projectee) with
| {lr_head = lr_head; lr_delta = lr_delta; lr_node = lr_node; lr_reject = lr_reject} -> begin
     lr_delta
     end))


let __proj__Mklane_rejection__item__lr_node = (fun ( projectee  :  lane_rejection<'op, 'rej> ) -> (match (projectee) with
| {lr_head = lr_head; lr_delta = lr_delta; lr_node = lr_node; lr_reject = lr_reject} -> begin
     lr_node
     end))


let __proj__Mklane_rejection__item__lr_reject = (fun ( projectee  :  lane_rejection<'op, 'rej> ) -> (match (projectee) with
| {lr_head = lr_head; lr_delta = lr_delta; lr_node = lr_node; lr_reject = lr_reject} -> begin
     lr_reject
     end))


let lr_head_of = (fun ( r  :  lane_rejection<'op, 'rej> ) -> r.lr_head)


let lr_delta_of = (fun ( r  :  lane_rejection<'op, 'rej> ) -> r.lr_delta)


let rec lanes_of = (fun ( d  :  dag<'op> ) ( ls  :  Prims.list<Prims.list<Prims.string>> ) -> (match (ls) with
| [] -> begin
     []
     end
| (l)::t -> begin
     ((ops_of (nodes_for d l)))::(lanes_of d t)
     end))


let rec lane_rejections = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( d  :  dag<'op> ) ( heads  :  Prims.list<Prims.string> ) ( lanes  :  Prims.list<Prims.list<Prims.string>> ) ( s  :  'state ) -> (match (((heads), (lanes))) with
| ((h)::hs, (l)::ls) -> begin
     (match ((replay_ids apply d.nodes l s)) with
| DagFold.Error (RRejected (n, e)) -> begin
     ({lr_head = h; lr_delta = (ops_of (nodes_for d l)); lr_node = n; lr_reject = e})::(lane_rejections apply d hs ls s)
     end
| uu___ -> begin
     (lane_rejections apply d hs ls s)
     end)
     end
| (uu___, uu___1) -> begin
     []
     end))


let lane_rejection_report = (fun ( apply  :  'op  ->  'state  ->  DagFold.outcome<'state, 'rej> ) ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( d  :  dag<'op> ) ( heads  :  Prims.list<Prims.string> ) ( lanes  :  Prims.list<Prims.list<Prims.string>> ) ( s  :  'state ) -> (DagFold.sort_by lt lr_head_of (lane_rejections apply d heads lanes s)))


let rec pairs : Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<(Prims.string * Prims.list<Prims.string>)> = (fun ( hs  :  Prims.list<Prims.string> ) ( ls  :  Prims.list<Prims.list<Prims.string>> ) -> (match (((hs), (ls))) with
| ((h)::hs', (l)::ls') -> begin
     (((h), (l)))::(pairs hs' ls')
     end
| (uu___, uu___1) -> begin
     []
     end))


let rec all_ueq = (fun ( c  :  DagFold.conflict<'op> ) ( cs  :  Prims.list<DagFold.conflict<'op>> ) -> (match (cs) with
| [] -> begin
     true
     end
| (d)::t -> begin
     ((DagFold.ueq c d) && (all_ueq c t))
     end))


let twin_lt : Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( x  :  Prims.string ) ( y  :  Prims.string ) -> ((((Prims.op_Equals x "a") && (not ((Prims.op_Equals y "a")))) || ((Prims.op_Equals x "b") && ((Prims.op_Equals y "c") || (Prims.op_Equals y "m")))) || ((Prims.op_Equals x "c") && (Prims.op_Equals y "m"))))


let twin_dag : dag<Prims.string> = {nodes = ({nid = "m"; nparents = ("b")::("c")::[]; nop = "m"})::({nid = "c"; nparents = ("a")::[]; nop = "c"})::({nid = "b"; nparents = ("a")::[]; nop = "b"})::({nid = "a"; nparents = []; nop = "a"})::[]}


let twin_fuel : Prims.list<node<Prims.string>> = (ListAlgebra.app twin_dag.nodes twin_dag.nodes)


let twin_bool_lt : Prims.bool  ->  Prims.bool  ->  Prims.bool = (fun ( p  :  Prims.bool ) ( q  :  Prims.bool ) -> ((not (p)) && q))


let twin_apply : Prims.string  ->  Prims.string  ->  DagFold.outcome<Prims.string, Prims.string> = (fun ( o  :  Prims.string ) ( s  :  Prims.string ) ->  
if (Prims.op_Equals o "x") then begin
     DagFold.Error ("refused")
     end else begin
     DagFold.Ok ((Prims.strcat s o))
     end)


let twin_cc : dag<Prims.string> = {nodes = ({nid = "y"; nparents = ("b")::("c")::[]; nop = "y"})::({nid = "x"; nparents = ("b")::("c")::[]; nop = "x"})::({nid = "c"; nparents = ("a")::[]; nop = "c"})::({nid = "b"; nparents = ("a")::[]; nop = "b"})::({nid = "a"; nparents = []; nop = "a"})::[]}


let twin_cc_fuel : Prims.list<node<Prims.string>> = (ListAlgebra.app twin_cc.nodes twin_cc.nodes)


let twin_chain : dag<Prims.string> = {nodes = ({nid = "r"; nparents = ("q")::[]; nop = "r"})::({nid = "q"; nparents = ("p")::[]; nop = "q"})::({nid = "p"; nparents = []; nop = "p"})::[]}


let twin_chain_fuel : Prims.list<node<Prims.string>> = (ListAlgebra.app twin_chain.nodes twin_chain.nodes)


let twin_fp : Prims.string  ->  DagFold.footprint = (fun ( uu___  :  Prims.string ) -> (whole_node_write_fp "n"))


let twin_seal : Prims.string  ->  Prims.string  ->  Prims.string = (fun ( n  :  Prims.string ) ( s  :  Prims.string ) -> (Prims.strcat n (Prims.strcat "|" s)))


let twin_sealed_at : Prims.string  ->  Prims.string  ->  DagFold.outcome<checkpoint<Prims.string>, ck_fault<Prims.string>>  ->  Prims.bool = (fun ( n  :  Prims.string ) ( st  :  Prims.string ) ( r  :  DagFold.outcome<checkpoint<Prims.string>, ck_fault<Prims.string>> ) -> (match (r) with
| DagFold.Ok (cp) -> begin
     (((Prims.op_Equals cp.cnode n) && (Prims.op_Equals cp.cstate st)) && (Prims.op_Equals cp.cseal (twin_seal n st)))
     end
| DagFold.Error (uu___) -> begin
     false
     end))


let twin_excl : Prims.list<Prims.list<Prims.string>> = (excl_lanes (drain_order twin_lt twin_fuel twin_dag.nodes) (ancestors_of twin_dag twin_fuel "a") (((ancestors_of twin_dag twin_fuel "b"))::((ancestors_of twin_dag twin_fuel "c"))::[]) (((ancestors_of twin_dag twin_fuel "b"))::((ancestors_of twin_dag twin_fuel "c"))::[]))


let twin_heads_report : Prims.list<DagFold.conflict<node<Prims.string>>> = (conflicts_of_heads twin_fp twin_dag (drain_order twin_lt twin_fuel twin_dag.nodes) (ancestors_of twin_dag twin_fuel "a") (((ancestors_of twin_dag twin_fuel "b"))::((ancestors_of twin_dag twin_fuel "c"))::[]))


let twins : Prims.list<Twin.twin> = ({Twin.tname = "independent-different-slots"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (DagFold.independent (slot_write_fp (("n"), ("tier"))) (slot_write_fp (("n"), ("thinking")))) true))})::({Twin.tname = "dependent-same-slot"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (DagFold.independent (slot_write_fp (("n"), ("tier"))) (slot_write_fp (("n"), ("tier")))) false))})::({Twin.tname = "whole-node-write-refuses-a-slot"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (DagFold.independent (whole_node_write_fp "n") (slot_write_fp (("n"), ("tier")))) false))})::({Twin.tname = "slot-clash-is-tagged-at-the-slot"; Twin.tholds = (fun ( uu___  :  unit ) -> (

let expected = {DagFold.left = (("n"), ("tier")); DagFold.right = (("n"), ("tier")); DagFold.address = "n"; DagFold.shape = DagFold.SlotClash ("tier")}
in (

let cs = (DagFold.pair_conflicts slot_write_fp (("n"), ("tier")) (("n"), ("tier")))
in ((DagFold.mem_u expected cs) && (all_ueq expected cs)))))})::({Twin.tname = "different-slots-fold-clean"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (DagFold.pair_conflicts slot_write_fp (("n"), ("tier")) (("n"), ("thinking"))) []))})::({Twin.tname = "slot-writes-commute-at-every-slot"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (lookup_slot (write_slot (("n"), ("tier")) "t2" (write_slot (("n"), ("thinking")) "high" [])) (("n"), ("thinking"))) (lookup_slot (write_slot (("n"), ("thinking")) "high" (write_slot (("n"), ("tier")) "t2" [])) (("n"), ("thinking")))))})::({Twin.tname = "drain-orders-a-diamond-by-the-tie-break"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (drain_order twin_lt twin_fuel twin_dag.nodes) (("a")::("b")::("c")::("m")::[])))})::({Twin.tname = "drain-refuses-a-dangling-parent"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (drain RefuseDangling twin_lt twin_fuel (({nid = "c"; nparents = ("z")::[]; nop = "c"})::[])) (Refused ("c"))))})::({Twin.tname = "replay-to-folds-the-drained-closure"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (replay_to twin_apply twin_lt twin_fuel twin_dag twin_fuel "" "m") (DagFold.Ok ("abcm"))))})::({Twin.tname = "replay-to-refuses-an-unknown-head"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (replay_to twin_apply twin_lt twin_fuel twin_dag twin_fuel "" "zz") (DagFold.Error (RUnknownHead ("zz")))))})::({Twin.tname = "add-node-refuses-a-differing-node"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (add_node (fun ( p  :  node<Prims.string> ) ( q  :  node<Prims.string> ) -> (Prims.op_Equals p.nop q.nop)) {nid = "a"; nparents = []; nop = "z"} twin_dag) (Collision ("a"))))})::({Twin.tname = "total-order-by-takes-the-key-before-the-id"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (drain_order (by_key twin_dag.nodes (fun ( n  :  node<Prims.string> ) -> (Prims.op_Equals n.nid "b")) twin_bool_lt twin_lt) twin_fuel twin_dag.nodes) (("a")::("c")::("b")::("m")::[])))})::({Twin.tname = "conflicts-of-heads-is-the-lane-sweep-node-for-op"; Twin.tholds = (fun ( uu___  :  unit ) -> ((not ((Prims.op_Equals twin_heads_report []))) && (Prims.op_Equals (map_list (map_conflict node_op) twin_heads_report) (DagFold.all_conflicts twin_fp (map_list (fun ( ids  :  Prims.list<Prims.string> ) -> (ops_of (nodes_for twin_dag ids))) twin_excl)))))})::({Twin.tname = "reach-index-answers-the-walk"; Twin.tholds = (fun ( uu___  :  unit ) -> (

let r = (reach_of_dag twin_lt twin_fuel twin_dag)
in ((((Prims.op_Equals (reach_reaches twin_dag twin_fuel r "a" "m") true) && (Prims.op_Equals (reach_reaches twin_dag twin_fuel r "b" "c") false)) && (Prims.op_Equals (reach_reaches twin_dag twin_fuel r "zz" "m") false)) && (Prims.op_Equals (reach_ancestors twin_dag twin_fuel r "c") (("c")::("a")::[])))))})::({Twin.tname = "checkpoint-at-seals-the-node-it-folded"; Twin.tholds = (fun ( uu___  :  unit ) -> (twin_sealed_at "b" "ab" (checkpoint_at twin_apply twin_lt twin_fuel twin_dag twin_fuel twin_seal "" "b")))})::({Twin.tname = "replay-from-a-checkpoint-is-replay-from-genesis"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (replay_from twin_apply twin_lt twin_fuel twin_dag twin_fuel (seal_at twin_seal "a" "a") "m") (DagFold.Ok ("abcm"))))})::({Twin.tname = "replay-from-refuses-a-branch-from-below-the-checkpoint"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (replay_from twin_apply twin_lt twin_fuel twin_dag twin_fuel (seal_at twin_seal "b" "ab") "m") (DagFold.Error (CUncovered ("c")))))})::({Twin.tname = "lane-rejection-report-is-sorted-by-head"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (lane_rejection_report twin_apply twin_lt twin_cc (("c")::("b")::[]) ((("c")::("x")::[])::(("x")::[])::[]) "s") (({lr_head = "b"; lr_delta = ("x")::[]; lr_node = "x"; lr_reject = "refused"})::({lr_head = "c"; lr_delta = ("c")::("x")::[]; lr_node = "x"; lr_reject = "refused"})::[])))})::({Twin.tname = "truncation-keeps-the-replay"; Twin.tholds = (fun ( uu___  :  unit ) -> (

let cp = (seal_at twin_seal "q" "pq")
in (((truncation_ok twin_chain twin_chain_fuel "q" (("q")::("r")::[])) && (Prims.op_Equals (replay_from twin_apply twin_lt twin_chain_fuel (truncate twin_chain (("q")::("r")::[])) twin_chain_fuel cp "r") (DagFold.Ok ("pqr")))) && (Prims.op_Equals (replay_from twin_apply twin_lt twin_chain_fuel twin_chain twin_chain_fuel cp "r") (DagFold.Ok ("pqr"))))))})::[]




