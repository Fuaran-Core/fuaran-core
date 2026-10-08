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


let resolve_in = (fun ( results  :  Prims.list<(Prims.string * 'v)> ) ( k  :  Prims.string ) -> (assoc k results))


let rec lookups = (fun ( rs  :  Prims.list<Prims.string> ) ( results  :  Prims.list<(Prims.string * 'v)> ) -> (match (rs) with
| [] -> begin
     []
     end
| (r)::t -> begin
     (match ((assoc r results)) with
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
      
if (mem k (reads_of deps id)) then begin
     (first_undeclared deps id t)
     end else begin
     FStar_Pervasives_Native.Some (k)
     end
     end))


let always : Prims.string  ->  Prims.bool = (fun ( uu___  :  Prims.string ) -> true)


let in_set : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( s  :  Prims.list<Prims.string> ) ( id  :  Prims.string ) -> (mem id s))


let reuse = (fun ( recompute  :  Prims.string  ->  Prims.bool ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( id  :  Prims.string ) ->  
if (recompute id) then begin
     FStar_Pervasives_Native.None
     end else begin
     (assoc id prior)
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


let eval = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (walk ev touches deps always [] topo))


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


let eval_from = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (match ((unknown_of deps changed)) with
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


let walk_invoked = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (match ((unknown_of deps changed)) with
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
     (match ((evw (resolve_in (lookups (reads_of deps id) results)) (assoc id prior) id)) with
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


let eval_with = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (walk_with evw touches deps always [] topo))


let eval_from_with = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( prior  :  Prims.list<(Prims.string * 'v)> ) ( changed  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (match ((unknown_of deps changed)) with
| (uu___)::uu___1 -> begin
     Error (EvalUnknownChange ((unknown_of deps changed)))
     end
| [] -> begin
     (walk_with evw touches deps (in_set (dirty_from_changed_ids deps changed)) prior topo)
     end))


let needed_for : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( deps  :  dmap ) ( targets  :  Prims.list<Prims.string> ) -> (grow deps targets targets))


let rec keep : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( s  :  Prims.list<Prims.string> ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (mem h s) then begin
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
     ((mem h s) || (meets s t))
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
      
if (mem k s) then begin
     (((k), (x)))::(keep_values s t)
     end else begin
     (keep_values s t)
     end
     end))


let eval_for_with = (fun ( evw  :  evaluator_with<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (walk_with evw touches deps always [] (restrict_topo (needed_for deps targets) topo)))


let eval_for = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (eval_for_with (lift ev) touches targets deps topo))


let walk_for_invoked = (fun ( ev  :  evaluator<'v> ) ( touches  :  read_witness ) ( targets  :  Prims.list<Prims.string> ) ( deps  :  dmap ) ( topo  :  topo_result ) -> (go_invoked ev touches deps always [] [] (keep (needed_for deps targets) topo.order)))


let rec concat_all : Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.string> = (fun ( gs  :  Prims.list<Prims.list<Prims.string>> ) -> (match (gs) with
| [] -> begin
     []
     end
| (g)::r -> begin
     (app g (concat_all r))
     end))


let rec keys : dmap  ->  Prims.list<Prims.string> = (fun ( d  :  dmap ) -> (match (d) with
| [] -> begin
     []
     end
| ((k, uu___))::r -> begin
     (k)::(keys r)
     end))


let same_set : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( a  :  Prims.list<Prims.string> ) ( b  :  Prims.list<Prims.string> ) -> ((subset a b) && (subset b a)))


let rec reads_ok : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( deps  :  dmap ) ( cyc  :  Prims.list<Prims.string> ) ( seen  :  Prims.list<Prims.string> ) ( rs  :  Prims.list<Prims.string> ) -> (match (rs) with
| [] -> begin
     true
     end
| (r)::rest -> begin
     ((((not ((has_key r deps))) || (mem r seen)) || (mem r cyc)) && (reads_ok deps cyc seen rest))
     end))


let rec ordered : dmap  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( deps  :  dmap ) ( cyc  :  Prims.list<Prims.string> ) ( seen  :  Prims.list<Prims.string> ) ( order  :  Prims.list<Prims.string> ) -> (match (order) with
| [] -> begin
     true
     end
| (id)::rest -> begin
     ((reads_ok deps cyc seen (reads_of deps id)) && (ordered deps cyc ((id)::seen) rest))
     end))


let valid_topo : dmap  ->  topo_result  ->  Prims.bool = (fun ( deps  :  dmap ) ( topo  :  topo_result ) -> (

let cyc = (concat_all topo.cycles)
in (

let all = (app topo.order cyc)
in (((distinct all) && (same_set all (keys deps))) && (ordered deps cyc [] topo.order)))))

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


let twin_deps : dmap = ((("b"), (("a")::[])))::((("c"), (("b")::[])))::[]


let twins : Prims.list<twin> = ({tname = "dependents-inverts-the-edges"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (dependents twin_deps) (((("a"), (("b")::[])))::((("b"), (("c")::[])))::[])))})::({tname = "dirty-set-is-the-downstream-closure"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (dirty_from_changed_ids twin_deps (("a")::[])) (("a")::("b")::("c")::[])))})::({tname = "an-edge-has-a-direction"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (edge twin_deps "b" "c") false))})::({tname = "the-certificate-accepts-a-dependency-order"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (valid_topo twin_deps {order = ("b")::("c")::[]; cycles = []}) true))})::({tname = "the-certificate-refuses-a-reader-before-its-read"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (valid_topo twin_deps {order = ("c")::("b")::[]; cycles = []}) false))})::[]




