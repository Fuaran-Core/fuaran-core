module Relation

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


let rec subset : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( a  :  Prims.list<Prims.string> ) ( b  :  Prims.list<Prims.string> ) -> (match (a) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((ListAlgebra.mem h b) && (subset t b))
     end))


let union : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( a  :  Prims.list<Prims.string> ) ( b  :  Prims.list<Prims.string> ) -> (ListAlgebra.app a (ListAlgebra.diff b a)))


let rec dedup : Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (ListAlgebra.mem h t) then begin
     (dedup t)
     end else begin
     (h)::(dedup t)
     end
     end))


let rec concat : Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.string> = (fun ( gs  :  Prims.list<Prims.list<Prims.string>> ) -> (match (gs) with
| [] -> begin
     []
     end
| (g)::r -> begin
     (ListAlgebra.app g (concat r))
     end))


let same_set : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( a  :  Prims.list<Prims.string> ) ( b  :  Prims.list<Prims.string> ) -> ((subset a b) && (subset b a)))


type relation = Prims.list<(Prims.string * Prims.list<Prims.string>)>


let succ_of : relation  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) ( n  :  Prims.string ) -> (match ((assoc n r)) with
| FStar_Pervasives_Native.Some (l) -> begin
     l
     end
| FStar_Pervasives_Native.None -> begin
     []
     end))


let rec range : relation  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) -> (match (r) with
| [] -> begin
     []
     end
| ((uu___, vs))::t -> begin
     (ListAlgebra.app vs (range t))
     end))


let rec nub : Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
     (h)::(nub (ListAlgebra.diff t ((h)::[])))
     end))


let nodes : relation  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) -> (nub (ListAlgebra.app (keys r) (range r))))


let rec preds_of : relation  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) ( ns  :  Prims.list<Prims.string> ) ( b  :  Prims.string ) -> (match (ns) with
| [] -> begin
     []
     end
| (a)::t -> begin
      
if (ListAlgebra.mem b (succ_of r a)) then begin
     (a)::(preds_of r t b)
     end else begin
     (preds_of r t b)
     end
     end))


let rec inverse_over : relation  ->  Prims.list<Prims.string>  ->  relation = (fun ( r  :  relation ) ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     []
     end
| (b)::t -> begin
     (((b), ((preds_of r (nodes r) b))))::(inverse_over r t)
     end))


let inverse : relation  ->  relation = (fun ( r  :  relation ) -> (inverse_over r (nodes r)))


let rec next_of : relation  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( d  :  relation ) ( frontier  :  Prims.list<Prims.string> ) ( s  :  Prims.list<Prims.string> ) -> (match (frontier) with
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


let rec any_dep : relation  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( d  :  relation ) ( frontier  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (match (frontier) with
| [] -> begin
     false
     end
| (node)::t -> begin
     ((ListAlgebra.mem x (succ_of d node)) || (any_dep d t x))
     end))


let rec grow : relation  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( d  :  relation ) ( frontier  :  Prims.list<Prims.string> ) ( acc  :  Prims.list<Prims.string> ) -> (match (frontier) with
| [] -> begin
     acc
     end
| (uu___)::uu___1 -> begin
     (

let next = (next_of d frontier [])
in (

let fresh = (ListAlgebra.diff next acc)
in (grow d fresh (union acc fresh))))
     end))


let reachable : relation  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( d  :  relation ) ( sources  :  Prims.list<Prims.string> ) -> (grow d sources sources))


let reach_plus : relation  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) ( x  :  Prims.string ) -> (reachable r (succ_of r x)))


let on_cycle : relation  ->  Prims.string  ->  Prims.bool = (fun ( r  :  relation ) ( x  :  Prims.string ) -> (ListAlgebra.mem x (reach_plus r x)))


let mutual : relation  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( r  :  relation ) ( x  :  Prims.string ) ( y  :  Prims.string ) -> ((ListAlgebra.mem y (reachable r ((x)::[]))) && (ListAlgebra.mem x (reachable r ((y)::[])))))


let rec cyclic_nodes : relation  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
      
if (on_cycle r n) then begin
     (n)::(cyclic_nodes r t)
     end else begin
     (cyclic_nodes r t)
     end
     end))


let cycles_among_spec : relation  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) -> (cyclic_nodes r (nodes r)))


let rec any_on_cycle : relation  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( r  :  relation ) ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     false
     end
| (n)::t -> begin
     ((on_cycle r n) || (any_on_cycle r t))
     end))


let rec sinks_among : relation  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
      
if (match ((succ_of r n)) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     (n)::(sinks_among r t)
     end else begin
     (sinks_among r t)
     end
     end))


let terminals_spec : relation  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>> = (fun ( r  :  relation ) ( x  :  Prims.string ) -> (

let reached = (reachable r ((x)::[]))
in  
if (any_on_cycle r reached) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some ((sinks_among r reached))
     end))


let rec succs_within : relation  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( r  :  relation ) ( allowed  :  Prims.list<Prims.string> ) ( ms  :  Prims.list<Prims.string> ) -> (match (ms) with
| [] -> begin
     true
     end
| (m)::t -> begin
     ((subset (succ_of r m) allowed) && (succs_within r allowed t))
     end))


let rec deps_first : relation  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.bool = (fun ( r  :  relation ) ( earlier  :  Prims.list<Prims.string> ) ( comps  :  Prims.list<Prims.list<Prims.string>> ) -> (match (comps) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((succs_within r (ListAlgebra.app c earlier) c) && (deps_first r (ListAlgebra.app c earlier) t))
     end))

type state = {idx : Prims.list<(Prims.string * Prims.nat)>; low : Prims.list<(Prims.string * Prims.nat)>; stack : Prims.list<Prims.string>; comps : Prims.list<Prims.list<Prims.string>>; next : Prims.nat}


let __proj__Mkstate__item__idx : state  ->  Prims.list<(Prims.string * Prims.nat)> = (fun ( projectee  :  state ) -> (match (projectee) with
| {idx = idx; low = low; stack = stack; comps = comps; next = next} -> begin
     idx
     end))


let __proj__Mkstate__item__low : state  ->  Prims.list<(Prims.string * Prims.nat)> = (fun ( projectee  :  state ) -> (match (projectee) with
| {idx = idx; low = low; stack = stack; comps = comps; next = next} -> begin
     low
     end))


let __proj__Mkstate__item__stack : state  ->  Prims.list<Prims.string> = (fun ( projectee  :  state ) -> (match (projectee) with
| {idx = idx; low = low; stack = stack; comps = comps; next = next} -> begin
     stack
     end))


let __proj__Mkstate__item__comps : state  ->  Prims.list<Prims.list<Prims.string>> = (fun ( projectee  :  state ) -> (match (projectee) with
| {idx = idx; low = low; stack = stack; comps = comps; next = next} -> begin
     comps
     end))


let __proj__Mkstate__item__next : state  ->  Prims.nat = (fun ( projectee  :  state ) -> (match (projectee) with
| {idx = idx; low = low; stack = stack; comps = comps; next = next} -> begin
     next
     end))


let init : state = {idx = []; low = []; stack = []; comps = []; next = (Prims.parse_int "0")}


let visited : state  ->  Prims.string  ->  Prims.bool = (fun ( st  :  state ) ( v  :  Prims.string ) -> (has_key v st.idx))


let idx_of : state  ->  Prims.string  ->  Prims.nat = (fun ( st  :  state ) ( v  :  Prims.string ) -> (match ((assoc v st.idx)) with
| FStar_Pervasives_Native.Some (n) -> begin
     n
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end))


let low_of : state  ->  Prims.string  ->  Prims.nat = (fun ( st  :  state ) ( v  :  Prims.string ) -> (match ((assoc v st.low)) with
| FStar_Pervasives_Native.Some (n) -> begin
     n
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end))


let min : Prims.nat  ->  Prims.nat  ->  Prims.nat = (fun ( a  :  Prims.nat ) ( b  :  Prims.nat ) ->  
if (a <= b) then begin
     a
     end else begin
     b
     end)


let set_low : state  ->  Prims.string  ->  Prims.nat  ->  state = (fun ( st  :  state ) ( v  :  Prims.string ) ( n  :  Prims.nat ) -> {idx = st.idx; low = (((v), (n)))::st.low; stack = st.stack; comps = st.comps; next = st.next})


let enter : state  ->  Prims.string  ->  state = (fun ( st  :  state ) ( v  :  Prims.string ) -> {idx = (((v), (st.next)))::st.idx; low = (((v), (st.next)))::st.low; stack = (v)::st.stack; comps = st.comps; next = (st.next + (Prims.parse_int "1"))})


let rec pop_until : Prims.string  ->  Prims.list<Prims.string>  ->  (Prims.list<Prims.string> * Prims.list<Prims.string>) = (fun ( v  :  Prims.string ) ( stack  :  Prims.list<Prims.string> ) -> (match (stack) with
| [] -> begin
     (([]), ([]))
     end
| (w)::rest -> begin
      
if (Prims.op_Equals w v) then begin
     (((v)::[]), (rest))
     end else begin
     (

let uu___ = (pop_until v rest)
in (match (uu___) with
| (ms, below) -> begin
     (((w)::ms), (below))
     end))
     end
     end))


let rec prefix_to : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>> = (fun ( s  :  Prims.list<Prims.string> ) ( l  :  Prims.list<Prims.string> ) ->  
if (Prims.op_Equals s l) then begin
     FStar_Pervasives_Native.Some ([])
     end else begin
     (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (h)::t -> begin
     (match ((prefix_to s t)) with
| FStar_Pervasives_Native.Some (a) -> begin
     FStar_Pervasives_Native.Some ((h)::a)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end)


let suffix : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( s  :  Prims.list<Prims.string> ) ( l  :  Prims.list<Prims.string> ) -> (match ((prefix_to s l)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let rec last : Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::[] -> begin
     FStar_Pervasives_Native.Some (x)
     end
| (uu___)::t -> begin
     (last t)
     end))


let rec visit : relation  ->  state  ->  Prims.string  ->  state = (fun ( r  :  relation ) ( st  :  state ) ( v  :  Prims.string ) -> (

let st1 = (enter st v)
in (

let st2 = (visit_succs r st1 v (succ_of r v))
in  
if (Prims.op_Equals (low_of st2 v) (idx_of st2 v)) then begin
     (

let above = (match ((prefix_to ((v)::st.stack) st2.stack)) with
| FStar_Pervasives_Native.Some (v1) -> begin
     v1
     end)
in (

let uu___ = (pop_until v st2.stack)
in (match (uu___) with
| (members, below) -> begin
     {idx = st2.idx; low = st2.low; stack = below; comps = (members)::st2.comps; next = st2.next}
     end)))
     end else begin
     st2
     end)))
and visit_succs : relation  ->  state  ->  Prims.string  ->  Prims.list<Prims.string>  ->  state = (fun ( r  :  relation ) ( st  :  state ) ( v  :  Prims.string ) ( succs  :  Prims.list<Prims.string> ) -> (match (succs) with
| [] -> begin
     st
     end
| (w)::rest -> begin
      
if (not ((visited st w))) then begin
     (

let st1 = (visit r st w)
in (

let st2 = (set_low st1 v (min (low_of st1 v) (low_of st1 w)))
in (

let st3 = (visit_succs r st2 v rest)
in st3)))
     end else begin
      
if (ListAlgebra.mem w st.stack) then begin
     (

let st1 = (set_low st v (min (low_of st v) (idx_of st w)))
in (

let st2 = (visit_succs r st1 v rest)
in st2))
     end else begin
     (visit_succs r st v rest)
     end
     end
     end))


let rec roots : relation  ->  state  ->  Prims.list<Prims.string>  ->  state = (fun ( r  :  relation ) ( st  :  state ) ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     st
     end
| (v)::t -> begin
      
if (visited st v) then begin
     (roots r st t)
     end else begin
     (

let st1 = (visit r st v)
in (roots r st1 t))
     end
     end))


let pass : relation  ->  Prims.list<Prims.list<Prims.string>> = (fun ( r  :  relation ) -> (ListAlgebra.rev (roots r init (nodes r)).comps))


let is_cycle : relation  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( r  :  relation ) ( members  :  Prims.list<Prims.string> ) -> (match (members) with
| [] -> begin
     false
     end
| (v)::[] -> begin
     (ListAlgebra.mem v (succ_of r v))
     end
| uu___ -> begin
     true
     end))


let components : relation  ->  Prims.list<Prims.list<Prims.string>> = (fun ( r  :  relation ) -> (pass r))


let rec cyclic_of : relation  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.list<Prims.string>> = (fun ( r  :  relation ) ( comps  :  Prims.list<Prims.list<Prims.string>> ) -> (match (comps) with
| [] -> begin
     []
     end
| (c)::t -> begin
      
if (is_cycle r c) then begin
     (c)::(cyclic_of r t)
     end else begin
     (cyclic_of r t)
     end
     end))


let cycles : relation  ->  Prims.list<Prims.list<Prims.string>> = (fun ( r  :  relation ) -> (cyclic_of r (pass r)))


let cycles_among : relation  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) -> (concat (cycles r)))

type topo = {torder : Prims.list<Prims.string>; tcycles : Prims.list<Prims.list<Prims.string>>}


let __proj__Mktopo__item__torder : topo  ->  Prims.list<Prims.string> = (fun ( projectee  :  topo ) -> (match (projectee) with
| {torder = torder; tcycles = tcycles} -> begin
     torder
     end))


let __proj__Mktopo__item__tcycles : topo  ->  Prims.list<Prims.list<Prims.string>> = (fun ( projectee  :  topo ) -> (match (projectee) with
| {torder = torder; tcycles = tcycles} -> begin
     tcycles
     end))


let rec order_of : relation  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<Prims.string> = (fun ( r  :  relation ) ( comps  :  Prims.list<Prims.list<Prims.string>> ) -> (match (comps) with
| [] -> begin
     []
     end
| (c)::t -> begin
      
if (is_cycle r c) then begin
     (order_of r t)
     end else begin
     (match (c) with
| (v)::uu___ -> begin
     (v)::(order_of r t)
     end
| [] -> begin
     (order_of r t)
     end)
     end
     end))


let sort : relation  ->  topo = (fun ( r  :  relation ) -> (

let found = (pass r)
in {torder = (order_of r found); tcycles = (cyclic_of r found)}))


let ends_of : Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)>  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>> = (fun ( ends  :  Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)> ) ( w  :  Prims.string ) -> (match ((assoc w ends)) with
| FStar_Pervasives_Native.Some (a) -> begin
     a
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end))


let rec join_ends : Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)>  ->  Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>> = (fun ( ends  :  Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)> ) ( ws  :  Prims.list<Prims.string> ) ( acc  :  FStar_Pervasives_Native.option<Prims.list<Prims.string>> ) -> (match (ws) with
| [] -> begin
     acc
     end
| (w)::t -> begin
     (join_ends ends t (match (((acc), ((ends_of ends w)))) with
| (FStar_Pervasives_Native.Some (sofar), FStar_Pervasives_Native.Some (theirs)) -> begin
     FStar_Pervasives_Native.Some ((union sofar theirs))
     end
| (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end))
     end))


let rec bind_all : Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>>  ->  Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)>  ->  Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)> = (fun ( ms  :  Prims.list<Prims.string> ) ( a  :  FStar_Pervasives_Native.option<Prims.list<Prims.string>> ) ( ends  :  Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)> ) -> (match (ms) with
| [] -> begin
     ends
     end
| (m)::t -> begin
     (bind_all t a ((((m), (a)))::ends))
     end))


let rec answer_all : relation  ->  Prims.list<Prims.list<Prims.string>>  ->  Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)>  ->  Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)> = (fun ( r  :  relation ) ( comps  :  Prims.list<Prims.list<Prims.string>> ) ( ends  :  Prims.list<(Prims.string * FStar_Pervasives_Native.option<Prims.list<Prims.string>>)> ) -> (match (comps) with
| [] -> begin
     ends
     end
| (members)::t -> begin
     (

let a =  
if (is_cycle r members) then begin
     FStar_Pervasives_Native.None
     end else begin
     (match (members) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (v)::uu___ -> begin
     (match ((succ_of r v)) with
| [] -> begin
     FStar_Pervasives_Native.Some ((v)::[])
     end
| ss -> begin
     (join_ends ends ss (FStar_Pervasives_Native.Some ([])))
     end)
     end)
     end
in (answer_all r t (bind_all members a ends)))
     end))


let terminals : relation  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>> = (fun ( r  :  relation ) -> (

let ends = (answer_all r (pass r) [])
in (fun ( node  :  Prims.string ) ->  
if (ListAlgebra.mem node (nodes r)) then begin
     (ends_of ends node)
     end else begin
     FStar_Pervasives_Native.Some ((node)::[])
     end)))


let twin_rel : relation = ((("a"), (("b")::[])))::((("b"), (("a")::("c")::[])))::((("c"), ([])))::[]


let twins : Prims.list<Twin.twin> = ({Twin.tname = "components-come-dependencies-first-in-pop-order"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (components twin_rel) ((("c")::[])::(("b")::("a")::[])::[])))})::({Twin.tname = "sort-splits-singletons-from-cycles"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (sort twin_rel) {torder = ("c")::[]; tcycles = (("b")::("a")::[])::[]}))})::({Twin.tname = "cycles-among-is-the-cyclic-members"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (cycles_among twin_rel) (("b")::("a")::[])) && (Prims.op_Equals (cycles_among_spec twin_rel) (("a")::("b")::[]))))})::({Twin.tname = "reachable-closes-forward"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (reachable twin_rel (("c")::[])) (("c")::[])) && (Prims.op_Equals (reachable twin_rel (("b")::[])) (("b")::("a")::("c")::[]))))})::({Twin.tname = "terminals-end-at-sinks-or-nowhere"; Twin.tholds = (fun ( uu___  :  unit ) -> ((((Prims.op_Equals (terminals twin_rel "a") FStar_Pervasives_Native.None) && (Prims.op_Equals (terminals twin_rel "c") (FStar_Pervasives_Native.Some (("c")::[])))) && (Prims.op_Equals (terminals twin_rel "z") (FStar_Pervasives_Native.Some (("z")::[])))) && (Prims.op_Equals (terminals_spec twin_rel "a") FStar_Pervasives_Native.None)))})::({Twin.tname = "inverse-turns-every-edge-round"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (inverse twin_rel) (((("a"), (("b")::[])))::((("b"), (("a")::[])))::((("c"), (("b")::[])))::[])))})::({Twin.tname = "the-specification-reads-the-twin-as-the-pass-does"; Twin.tholds = (fun ( uu___  :  unit ) -> (((deps_first twin_rel [] (components twin_rel)) && (mutual twin_rel "a" "b")) && (not ((on_cycle twin_rel "c")))))})::[]




