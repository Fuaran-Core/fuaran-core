module Propagation
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


type dmap = Prims.list<(Prims.string * Prims.list<Prims.string>)>


let reads_of : dmap  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( id  :  Prims.string ) -> (Relation.succ_of deps id))


let rec edge : dmap  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( deps  :  dmap ) ( n  :  Prims.string ) ( r  :  Prims.string ) -> (match (deps) with
| [] -> begin
     false
     end
| ((k, reads))::t -> begin
     (((Prims.op_Equals k n) && (ListAlgebra.mem r reads)) || (edge t n r))
     end))


let rec pairs_of : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( node  :  Prims.string ) ( reads  :  Prims.list<Prims.string> ) -> (match (reads) with
| [] -> begin
     []
     end
| (r)::t -> begin
     (((r), (node)))::(pairs_of node t)
     end))


let rec pairs : dmap  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( deps  :  dmap ) -> (match (deps) with
| [] -> begin
     []
     end
| ((node, reads))::t -> begin
     (ListAlgebra.app (pairs_of node reads) (pairs t))
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
     (((k), ((Relation.dedup (seconds_for k ps)))))::(group_from t ps)
     end))


let dependents : dmap  ->  dmap = (fun ( deps  :  dmap ) -> (

let ps = (pairs deps)
in (group_from (Relation.dedup (firsts ps)) ps)))


let dependents_of : dmap  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( d  :  dmap ) ( node  :  Prims.string ) -> (Relation.succ_of d node))


let rec mem_pair : Prims.string  ->  Prims.string  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.bool = (fun ( r  :  Prims.string ) ( n  :  Prims.string ) ( ps  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (ps) with
| [] -> begin
     false
     end
| ((r', n'))::t -> begin
     (((Prims.op_Equals r r') && (Prims.op_Equals n n')) || (mem_pair r n t))
     end))


let dirty_from_changed_ids : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( changed  :  Prims.list<Prims.string> ) -> (Relation.reachable (dependents deps) changed))


let rec downstream : dmap  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( deps  :  dmap ) ( c  :  Prims.string ) ( path  :  Prims.list<Prims.string> ) -> (match (path) with
| [] -> begin
     true
     end
| (n)::t -> begin
     ((edge deps n c) && (downstream deps n t))
     end))


let rec path_end : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.string = (fun ( c  :  Prims.string ) ( path  :  Prims.list<Prims.string> ) -> (match (path) with
| [] -> begin
     c
     end
| (n)::t -> begin
     (path_end n t)
     end))

type topo_result = {order : Prims.list<Prims.string>; cycles : Prims.list<Prims.list<Prims.string>>}


let __proj__Mktopo_result__item__order : topo_result  ->  Prims.list<Prims.string> = (fun ( projectee  :  topo_result ) -> (match (projectee) with
| {order = order; cycles = cycles} -> begin
     order
     end))


let __proj__Mktopo_result__item__cycles : topo_result  ->  Prims.list<Prims.list<Prims.string>> = (fun ( projectee  :  topo_result ) -> (match (projectee) with
| {order = order; cycles = cycles} -> begin
     cycles
     end))

type propagation_error =
| EvalUnknownChange of Prims.list<Prims.string>
| EvalNodeFailed of Prims.string * Prims.string
| EvalUndeclaredRead of Prims.string * Prims.string


let uu___is_EvalUnknownChange : propagation_error  ->  Prims.bool = (fun ( projectee  :  propagation_error ) -> (match (projectee) with
| EvalUnknownChange (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EvalUnknownChange__item___0 : propagation_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  propagation_error ) -> (match (projectee) with
| EvalUnknownChange (_0) -> begin
     _0
     end))


let uu___is_EvalNodeFailed : propagation_error  ->  Prims.bool = (fun ( projectee  :  propagation_error ) -> (match (projectee) with
| EvalNodeFailed (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EvalNodeFailed__item___0 : propagation_error  ->  Prims.string = (fun ( projectee  :  propagation_error ) -> (match (projectee) with
| EvalNodeFailed (_0, _1) -> begin
     _0
     end))


let __proj__EvalNodeFailed__item___1 : propagation_error  ->  Prims.string = (fun ( projectee  :  propagation_error ) -> (match (projectee) with
| EvalNodeFailed (_0, _1) -> begin
     _1
     end))


let uu___is_EvalUndeclaredRead : propagation_error  ->  Prims.bool = (fun ( projectee  :  propagation_error ) -> (match (projectee) with
| EvalUndeclaredRead (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EvalUndeclaredRead__item___0 : propagation_error  ->  Prims.string = (fun ( projectee  :  propagation_error ) -> (match (projectee) with
| EvalUndeclaredRead (_0, _1) -> begin
     _0
     end))


let __proj__EvalUndeclaredRead__item___1 : propagation_error  ->  Prims.string = (fun ( projectee  :  propagation_error ) -> (match (projectee) with
| EvalUndeclaredRead (_0, _1) -> begin
     _1
     end))

type eval_outcome<'v> = {values : Prims.list<(Prims.string * 'v)>; cyclic : Prims.list<Prims.list<Prims.string>>}


let __proj__Mkeval_outcome__item__values = (fun ( projectee  :  eval_outcome<'v> ) -> (match (projectee) with
| {values = values; cyclic = cyclic} -> begin
     values
     end))


let __proj__Mkeval_outcome__item__cyclic = (fun ( projectee  :  eval_outcome<'v> ) -> (match (projectee) with
| {values = values; cyclic = cyclic} -> begin
     cyclic
     end))


type evaluator<'v> = (Prims.string  ->  FStar_Pervasives_Native.option<'v>)  ->  Prims.string  ->  outcome<'v, Prims.string>


let resolve_in = (fun ( results  :  Prims.list<(Prims.string * 'v)> ) ( k  :  Prims.string ) -> (Relation.assoc k results))


let rec lookups = (fun ( rs  :  Prims.list<Prims.string> ) ( results  :  Prims.list<(Prims.string * 'v)> ) -> (match (rs) with
| [] -> begin
     []
     end
| (r)::t -> begin
     (match ((Relation.assoc r results)) with
| FStar_Pervasives_Native.Some (x) -> begin
     (((r), (x)))::(lookups t results)
     end
| FStar_Pervasives_Native.None -> begin
     (lookups t results)
     end)
     end))


type read_witness = Prims.string  ->  Prims.list<Prims.string>


let rec first_undeclared : dmap  ->  Prims.string  ->  Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( deps  :  dmap ) ( id  :  Prims.string ) ( touched  :  Prims.list<Prims.string> ) -> (match (touched) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (k)::t -> begin
      
if (ListAlgebra.mem k (reads_of deps id)) then begin
     (first_undeclared deps id t)
     end else begin
     FStar_Pervasives_Native.Some (k)
     end
     end))


let always : Prims.string  ->  Prims.bool = (fun ( uu___  :  Prims.string ) -> true)


let in_set : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( s  :  Prims.list<Prims.string> ) ( id  :  Prims.string ) -> (ListAlgebra.mem id s))


let reuse = (fun ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( id  :  Prims.string ) ->  
if (recompute id) then begin
     FStar_Pervasives_Native.None
     end else begin
     (Relation.assoc id prior)
     end)


let rec go = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( cycles  :  Prims.list<Prims.list<Prims.string>> ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( order  :  Prims.list<Prims.string> ) -> (match (order) with
| [] -> begin
     Ok ({values = results; cyclic = cycles})
     end
| (id)::rest -> begin
     (match ((reuse recompute prior id)) with
| FStar_Pervasives_Native.None -> begin
     (match ((first_undeclared deps id (touches id))) with
| FStar_Pervasives_Native.Some (r) -> begin
     Error (EvalUndeclaredRead (id, r))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((ev (resolve_in (lookups (reads_of deps id) results)) id)) with
| Ok (x) -> begin
     (go ev touches deps recompute prior cycles ((((id), (x)))::results) rest)
     end
| Error (m) -> begin
     Error (EvalNodeFailed (id, m))
     end)
     end)
     end
| FStar_Pervasives_Native.Some (p) -> begin
     (go ev touches deps recompute prior cycles ((((id), (p)))::results) rest)
     end)
     end))


let walk = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( topo  :  topo_result ) -> (go ev touches deps recompute prior topo.cycles [] topo.order))


let eval_over = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (walk ev touches deps always [] topo))


let rec unknown_of : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( changed  :  Prims.list<Prims.string> ) -> (match (changed) with
| [] -> begin
     []
     end
| (c)::t -> begin
      
if (Relation.has_key c deps) then begin
     (unknown_of deps t)
     end else begin
     (c)::(unknown_of deps t)
     end
     end))


let eval_from_over = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (match ((unknown_of deps changed)) with
| (uu___)::uu___1 -> begin
     Error (EvalUnknownChange ((unknown_of deps changed)))
     end
| [] -> begin
     (walk ev touches deps (in_set (dirty_from_changed_ids deps changed)) prior topo)
     end))


let rec go_invoked = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( order  :  Prims.list<Prims.string> ) -> (match (order) with
| [] -> begin
     []
     end
| (id)::rest -> begin
     (match ((reuse recompute prior id)) with
| FStar_Pervasives_Native.None -> begin
     (match ((first_undeclared deps id (touches id))) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     (id)::[]
     end
| FStar_Pervasives_Native.None -> begin
     (match ((ev (resolve_in (lookups (reads_of deps id) results)) id)) with
| Ok (x) -> begin
     (id)::(go_invoked ev touches deps recompute prior ((((id), (x)))::results) rest)
     end
| Error (uu___) -> begin
     (id)::[]
     end)
     end)
     end
| FStar_Pervasives_Native.Some (p) -> begin
     (go_invoked ev touches deps recompute prior ((((id), (p)))::results) rest)
     end)
     end))


let walk_invoked_over = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (match ((unknown_of deps changed)) with
| (uu___)::uu___1 -> begin
     []
     end
| [] -> begin
     (go_invoked ev touches deps (in_set (dirty_from_changed_ids deps changed)) prior [] topo.order)
     end))


type evaluator_with<'v> = (Prims.string  ->  FStar_Pervasives_Native.option<'v>)  ->  FStar_Pervasives_Native.option<'v>  ->  Prims.string  ->  outcome<'v, Prims.string>


let blind = (fun ( evw  :  evaluator_with<'v> ) ( f  :  Prims.string  ->  FStar_Pervasives_Native.option<'v> ) ( id  :  Prims.string ) -> (evw f FStar_Pervasives_Native.None id))


let lift = (fun ( ev  :  evaluator<'v> ) ( f  :  Prims.string  ->  FStar_Pervasives_Native.option<'v> ) ( uu___  :  FStar_Pervasives_Native.option<'v> ) ( id  :  Prims.string ) -> (ev f id))


let rec go_with = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( cycles  :  Prims.list<Prims.list<Prims.string>> ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( order  :  Prims.list<Prims.string> ) -> (match (order) with
| [] -> begin
     Ok ({values = results; cyclic = cycles})
     end
| (id)::rest -> begin
     (match ((reuse recompute prior id)) with
| FStar_Pervasives_Native.None -> begin
     (match ((first_undeclared deps id (touches id))) with
| FStar_Pervasives_Native.Some (r) -> begin
     Error (EvalUndeclaredRead (id, r))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((evw (resolve_in (lookups (reads_of deps id) results)) (Relation.assoc id prior) id)) with
| Ok (x) -> begin
     (go_with evw touches deps recompute prior cycles ((((id), (x)))::results) rest)
     end
| Error (m) -> begin
     Error (EvalNodeFailed (id, m))
     end)
     end)
     end
| FStar_Pervasives_Native.Some (p) -> begin
     (go_with evw touches deps recompute prior cycles ((((id), (p)))::results) rest)
     end)
     end))


let walk_with = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( topo  :  topo_result ) -> (go_with evw touches deps recompute prior topo.cycles [] topo.order))


let eval_with_over = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (walk_with evw touches deps always [] topo))


let eval_from_with_over = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (match ((unknown_of deps changed)) with
| (uu___)::uu___1 -> begin
     Error (EvalUnknownChange ((unknown_of deps changed)))
     end
| [] -> begin
     (walk_with evw touches deps (in_set (dirty_from_changed_ids deps changed)) prior topo)
     end))


let needed_for : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( targets  :  Prims.list<Prims.string> ) -> (Relation.reachable deps targets))


let rec keep : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( s  :  Prims.list<Prims.string> ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (ListAlgebra.mem h s) then begin
     (h)::(keep s t)
     end else begin
     (keep s t)
     end
     end))


let rec meets : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( s  :  Prims.list<Prims.string> ) ( g  :  Prims.list<Prims.string> ) -> (match (g) with
| [] -> begin
     false
     end
| (h)::t -> begin
     ((ListAlgebra.mem h s) || (meets s t))
     end))


let rec keep_groups : Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.list<Prims.string>> = (fun ( s  :  Prims.list<Prims.string> ) ( gs  :  Prims.list<Prims.list<Prims.string>> ) -> (match (gs) with
| [] -> begin
     []
     end
| (g)::t -> begin
      
if (meets s g) then begin
     (g)::(keep_groups s t)
     end else begin
     (keep_groups s t)
     end
     end))


let restrict_topo : Prims.list<Prims.string>  ->  topo_result  ->  topo_result = (fun ( s  :  Prims.list<Prims.string> ) ( topo  :  topo_result ) -> {order = (keep s topo.order); cycles = (keep_groups s topo.cycles)})


let rec keep_values = (fun ( s  :  Prims.list<Prims.string> ) ( l  :  Prims.list<(Prims.string * 'v)> ) -> (match (l) with
| [] -> begin
     []
     end
| ((k, x))::t -> begin
      
if (ListAlgebra.mem k s) then begin
     (((k), (x)))::(keep_values s t)
     end else begin
     (keep_values s t)
     end
     end))


let eval_for_with_over = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (walk_with evw touches deps always [] (restrict_topo (needed_for deps targets) topo)))


let eval_for_over = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (eval_for_with_over (lift ev) touches targets deps topo))


let walk_for_invoked_over = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (go_invoked ev touches deps always [] [] (keep (needed_for deps targets) topo.order)))


let rec reads_ok : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( deps  :  dmap ) ( cyc  :  Prims.list<Prims.string> ) ( seen  :  Prims.list<Prims.string> ) ( rs  :  Prims.list<Prims.string> ) -> (match (rs) with
| [] -> begin
     true
     end
| (r)::rest -> begin
     ((((not ((Relation.has_key r deps))) || (ListAlgebra.mem r seen)) || (ListAlgebra.mem r cyc)) && (reads_ok deps cyc seen rest))
     end))


let rec ordered : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( deps  :  dmap ) ( cyc  :  Prims.list<Prims.string> ) ( seen  :  Prims.list<Prims.string> ) ( order  :  Prims.list<Prims.string> ) -> (match (order) with
| [] -> begin
     true
     end
| (id)::rest -> begin
     ((reads_ok deps cyc seen (reads_of deps id)) && (ordered deps cyc ((id)::seen) rest))
     end))


let valid_topo : dmap  ->  topo_result  ->  Prims.bool = (fun ( deps  :  dmap ) ( topo  :  topo_result ) -> (

let cyc = (Relation.concat topo.cycles)
in (

let all = (ListAlgebra.app topo.order cyc)
in (((ListAlgebra.distinct all) && (Relation.same_set all (Relation.keys deps))) && (ordered deps cyc [] topo.order)))))


let rec known_reads : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( rs  :  Prims.list<Prims.string> ) -> (match (rs) with
| [] -> begin
     []
     end
| (r)::t -> begin
      
if (Relation.has_key r deps) then begin
     (r)::(known_reads deps t)
     end else begin
     (known_reads deps t)
     end
     end))


let rec known_of : dmap  ->  dmap  ->  dmap = (fun ( deps  :  dmap ) ( all  :  dmap ) -> (match (deps) with
| [] -> begin
     []
     end
| ((k, rs))::t -> begin
     (((k), ((known_reads all rs))))::(known_of t all)
     end))


let known : dmap  ->  dmap = (fun ( deps  :  dmap ) -> (known_of deps deps))


let sort : dmap  ->  topo_result = (fun ( deps  :  dmap ) -> (

let t = (Relation.sort (known deps))
in {order = t.torder; cycles = t.tcycles}))


let eval = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) -> (eval_over ev touches deps (sort deps)))


let eval_from = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) -> (eval_from_over ev touches prior changed deps (sort deps)))


let walk_invoked = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) -> (walk_invoked_over ev touches prior changed deps (sort deps)))


let eval_with = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) -> (eval_with_over evw touches deps (sort deps)))


let eval_from_with = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) -> (eval_from_with_over evw touches prior changed deps (sort deps)))


let eval_for_with = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) -> (eval_for_with_over evw touches targets deps (sort deps)))


let eval_for = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) -> (eval_for_over ev touches targets deps (sort deps)))


let walk_for_invoked = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) -> (walk_for_invoked_over ev touches targets deps (sort deps)))


let twin_deps : dmap = ((("b"), (("a")::[])))::((("c"), (("b")::[])))::[]


let twins : Prims.list<Twin.twin> = ({Twin.tname = "dependents-inverts-the-edges"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (dependents twin_deps) (((("a"), (("b")::[])))::((("b"), (("c")::[])))::[])))})::({Twin.tname = "dirty-set-is-the-downstream-closure"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (dirty_from_changed_ids twin_deps (("a")::[])) (("a")::("b")::("c")::[])))})::({Twin.tname = "an-edge-has-a-direction"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (edge twin_deps "b" "c") false))})::({Twin.tname = "the-certificate-accepts-a-dependency-order"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (valid_topo twin_deps {order = ("b")::("c")::[]; cycles = []}) true))})::({Twin.tname = "the-certificate-refuses-a-reader-before-its-read"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (valid_topo twin_deps {order = ("c")::("b")::[]; cycles = []}) false))})::({Twin.tname = "sort-computes-the-dependency-order-and-drops-a-dangling-read"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (sort twin_deps) {order = ("b")::("c")::[]; cycles = []}) && (Prims.op_Equals (known twin_deps) (((("b"), ([])))::((("c"), (("b")::[])))::[]))))})::[]




