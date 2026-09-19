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
| (h)::t -> begin
     (h)::(app t m)
     end))


let rec mem : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( x  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     false
     end
| (h)::t -> begin
     ((Prims.op_Equals x h) || (mem x t))
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


let rec distinct : Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((not ((mem h t))) && (distinct t))
     end))


let rec subset : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( a  :  Prims.list<Prims.string> ) ( b  :  Prims.list<Prims.string> ) -> (match (a) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((mem h b) && (subset t b))
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


type dmap = Prims.list<(Prims.string * Prims.list<Prims.string>)>


let reads_of : dmap  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( id  :  Prims.string ) -> (match ((assoc id deps)) with
| FStar_Pervasives_Native.Some (rs) -> begin
     rs
     end
| FStar_Pervasives_Native.None -> begin
     []
     end))


let rec edge : dmap  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( deps  :  dmap ) ( n  :  Prims.string ) ( r  :  Prims.string ) -> (match (deps) with
| [] -> begin
     false
     end
| ((k, reads))::t -> begin
     (((Prims.op_Equals k n) && (mem r reads)) || (edge t n r))
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
     (app (pairs_of node reads) (pairs t))
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


let dependents_of : dmap  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( d  :  dmap ) ( node  :  Prims.string ) -> (match ((assoc node d)) with
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
| (node)::t -> begin
     (next_of d t (match ((assoc node d)) with
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
| (node)::t -> begin
     ((mem x (dependents_of d node)) || (any_dep d t x))
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


let dirty_from_changed_ids : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( changed  :  Prims.list<Prims.string> ) -> (grow (dependents deps) changed changed))


let stale_set : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( changed  :  Prims.list<Prims.string> ) -> (dirty_from_changed_ids deps changed))


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


let resolve_in = (fun ( results  :  Prims.list<(Prims.string * 'v)> ) ( k  :  Prims.string ) -> (assoc k results))


let always : Prims.string  ->  Prims.bool = (fun ( uu___  :  Prims.string ) -> true)


let in_set : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( s  :  Prims.list<Prims.string> ) ( id  :  Prims.string ) -> (mem id s))


let reuse = (fun ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( id  :  Prims.string ) ->  
if (recompute id) then begin
     FStar_Pervasives_Native.None
     end else begin
     (assoc id prior)
     end)


let rec go = (fun ( ev  :  evaluator<'v> ) ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( cycles  :  Prims.list<Prims.list<Prims.string>> ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( order  :  Prims.list<Prims.string> ) -> (match (order) with
| [] -> begin
     Ok ({values = results; cyclic = cycles})
     end
| (id)::rest -> begin
     (match ((reuse recompute prior id)) with
| FStar_Pervasives_Native.None -> begin
     (match ((ev (resolve_in results) id)) with
| Ok (x) -> begin
     (go ev recompute prior cycles ((((id), (x)))::results) rest)
     end
| Error (m) -> begin
     Error (EvalNodeFailed (id, m))
     end)
     end
| FStar_Pervasives_Native.Some (p) -> begin
     (go ev recompute prior cycles ((((id), (p)))::results) rest)
     end)
     end))


let walk = (fun ( ev  :  evaluator<'v> ) ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( topo  :  topo_result ) -> (go ev recompute prior topo.cycles [] topo.order))


let eval = (fun ( ev  :  evaluator<'v> ) ( topo  :  topo_result ) -> (walk ev always [] topo))


let rec unknown_of : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( changed  :  Prims.list<Prims.string> ) -> (match (changed) with
| [] -> begin
     []
     end
| (c)::t -> begin
      
if (has_key c deps) then begin
     (unknown_of deps t)
     end else begin
     (c)::(unknown_of deps t)
     end
     end))


let eval_from = (fun ( ev  :  evaluator<'v> ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (match ((unknown_of deps changed)) with
| (uu___)::uu___1 -> begin
     Error (EvalUnknownChange ((unknown_of deps changed)))
     end
| [] -> begin
     (walk ev (in_set (dirty_from_changed_ids deps changed)) prior topo)
     end))


let rec go_invoked = (fun ( ev  :  evaluator<'v> ) ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( results  :  Prims.list<(Prims.string * 'v)> ) ( order  :  Prims.list<Prims.string> ) -> (match (order) with
| [] -> begin
     []
     end
| (id)::rest -> begin
     (match ((reuse recompute prior id)) with
| FStar_Pervasives_Native.None -> begin
     (match ((ev (resolve_in results) id)) with
| Ok (x) -> begin
     (id)::(go_invoked ev recompute prior ((((id), (x)))::results) rest)
     end
| Error (uu___) -> begin
     (id)::[]
     end)
     end
| FStar_Pervasives_Native.Some (p) -> begin
     (go_invoked ev recompute prior ((((id), (p)))::results) rest)
     end)
     end))


let walk_invoked = (fun ( ev  :  evaluator<'v> ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (match ((unknown_of deps changed)) with
| (uu___)::uu___1 -> begin
     []
     end
| [] -> begin
     (go_invoked ev (in_set (dirty_from_changed_ids deps changed)) prior [] topo.order)
     end))




