module TreeOps
type tree =
| TNode of Prims.string * Prims.string * Prims.list<tree>


let uu___is_TNode : tree  ->  Prims.bool = (fun ( projectee  :  tree ) -> true)


let __proj__TNode__item__tid : tree  ->  Prims.string = (fun ( projectee  :  tree ) -> (match (projectee) with
| TNode (tid, kind, kids) -> begin
     tid
     end))


let __proj__TNode__item__kind : tree  ->  Prims.string = (fun ( projectee  :  tree ) -> (match (projectee) with
| TNode (tid, kind, kids) -> begin
     kind
     end))


let __proj__TNode__item__kids : tree  ->  Prims.list<tree> = (fun ( projectee  :  tree ) -> (match (projectee) with
| TNode (tid, kind, kids) -> begin
     kids
     end))


let tid_of : tree  ->  Prims.string = (fun ( t  :  tree ) -> (match (t) with
| TNode (i, uu___, uu___1) -> begin
     i
     end))


let kind_of : tree  ->  Prims.string = (fun ( t  :  tree ) -> (match (t) with
| TNode (uu___, k, uu___1) -> begin
     k
     end))


let kids_of : tree  ->  Prims.list<tree> = (fun ( t  :  tree ) -> (match (t) with
| TNode (uu___, uu___1, cs) -> begin
     cs
     end))


let rec ids : tree  ->  Prims.list<Prims.string> = (fun ( t  :  tree ) -> (match (t) with
| TNode (i, uu___, cs) -> begin
     (i)::(ids_all cs)
     end))
and ids_all : Prims.list<tree>  ->  Prims.list<Prims.string> = (fun ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::r -> begin
     (DagFold.app (ids t) (ids_all r))
     end))


let rec kid_ids : Prims.list<tree>  ->  Prims.list<Prims.string> = (fun ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::r -> begin
     ((tid_of t))::(kid_ids r)
     end))


let has_id : Prims.string  ->  tree  ->  Prims.bool = (fun ( x  :  Prims.string ) ( t  :  tree ) -> (DagFold.mem x (ids t)))


let rec find_in : Prims.string  ->  tree  ->  FStar_Pervasives_Native.option<tree> = (fun ( x  :  Prims.string ) ( t  :  tree ) -> (match (t) with
| TNode (i, uu___, cs) -> begin
      
if (Prims.op_Equals i x) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     (find_all x cs)
     end
     end))
and find_all : Prims.string  ->  Prims.list<tree>  ->  FStar_Pervasives_Native.option<tree> = (fun ( x  :  Prims.string ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (t)::r -> begin
     (match ((find_in x t)) with
| FStar_Pervasives_Native.Some (n) -> begin
     FStar_Pervasives_Native.Some (n)
     end
| FStar_Pervasives_Native.None -> begin
     (find_all x r)
     end)
     end))


let rec has_kid : Prims.string  ->  Prims.list<tree>  ->  Prims.bool = (fun ( x  :  Prims.string ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     false
     end
| (t)::r -> begin
     ((Prims.op_Equals (tid_of t) x) || (has_kid x r))
     end))


let rec parent_of : Prims.string  ->  tree  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( x  :  Prims.string ) ( t  :  tree ) -> (match (t) with
| TNode (i, uu___, cs) -> begin
      
if (has_kid x cs) then begin
     FStar_Pervasives_Native.Some (i)
     end else begin
     (parent_all x cs)
     end
     end))
and parent_all : Prims.string  ->  Prims.list<tree>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( x  :  Prims.string ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (t)::r -> begin
     (match ((parent_of x t)) with
| FStar_Pervasives_Native.Some (p) -> begin
     FStar_Pervasives_Native.Some (p)
     end
| FStar_Pervasives_Native.None -> begin
     (parent_all x r)
     end)
     end))


let rec wf : tree  ->  Prims.bool = (fun ( t  :  tree ) -> (match (t) with
| TNode (i, uu___, cs) -> begin
     ((not ((DagFold.mem i (ids_all cs)))) && (wf_all cs))
     end))
and wf_all : Prims.list<tree>  ->  Prims.bool = (fun ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     true
     end
| (t)::r -> begin
     (((wf t) && (wf_all r)) && (DagFold.disjoint (ids t) (ids_all r)))
     end))

type rejection =
| UnknownNode of Prims.string * Prims.list<Prims.string>
| DuplicateId of Prims.string
| CannotRemoveRoot
| WouldNestUnderSelf of Prims.string
| NotAContainer of Prims.string * Prims.string
| ReorderMismatch of Prims.string * Prims.list<Prims.string> * Prims.list<Prims.string>
| Rejected of Prims.string * Prims.string


let uu___is_UnknownNode : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| UnknownNode (target, addressable) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownNode__item__target : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| UnknownNode (target, addressable) -> begin
     target
     end))


let __proj__UnknownNode__item__addressable : rejection  ->  Prims.list<Prims.string> = (fun ( projectee  :  rejection ) -> (match (projectee) with
| UnknownNode (target, addressable) -> begin
     addressable
     end))


let uu___is_DuplicateId : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| DuplicateId (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateId__item___0 : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| DuplicateId (_0) -> begin
     _0
     end))


let uu___is_CannotRemoveRoot : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| CannotRemoveRoot -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_WouldNestUnderSelf : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| WouldNestUnderSelf (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__WouldNestUnderSelf__item___0 : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| WouldNestUnderSelf (_0) -> begin
     _0
     end))


let uu___is_NotAContainer : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| NotAContainer (target, kind_tag) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NotAContainer__item__target : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| NotAContainer (target, kind_tag) -> begin
     target
     end))


let __proj__NotAContainer__item__kind_tag : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| NotAContainer (target, kind_tag) -> begin
     kind_tag
     end))


let uu___is_ReorderMismatch : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| ReorderMismatch (parent, expected, got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ReorderMismatch__item__parent : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| ReorderMismatch (parent, expected, got) -> begin
     parent
     end))


let __proj__ReorderMismatch__item__expected : rejection  ->  Prims.list<Prims.string> = (fun ( projectee  :  rejection ) -> (match (projectee) with
| ReorderMismatch (parent, expected, got) -> begin
     expected
     end))


let __proj__ReorderMismatch__item__got : rejection  ->  Prims.list<Prims.string> = (fun ( projectee  :  rejection ) -> (match (projectee) with
| ReorderMismatch (parent, expected, got) -> begin
     got
     end))


let uu___is_Rejected : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| Rejected (code, message) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Rejected__item__code : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| Rejected (code, message) -> begin
     code
     end))


let __proj__Rejected__item__message : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| Rejected (code, message) -> begin
     message
     end))

type op =
| InsertChild of Prims.string * tree
| RemoveNode of Prims.string
| MoveNode of Prims.string * Prims.string
| ReorderChildren of Prims.string * Prims.list<Prims.string>
| Batch of Prims.list<op>


let uu___is_InsertChild : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| InsertChild (parent, node) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__InsertChild__item__parent : op  ->  Prims.string = (fun ( projectee  :  op ) -> (match (projectee) with
| InsertChild (parent, node) -> begin
     parent
     end))


let __proj__InsertChild__item__node : op  ->  tree = (fun ( projectee  :  op ) -> (match (projectee) with
| InsertChild (parent, node) -> begin
     node
     end))


let uu___is_RemoveNode : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| RemoveNode (target) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RemoveNode__item__target : op  ->  Prims.string = (fun ( projectee  :  op ) -> (match (projectee) with
| RemoveNode (target) -> begin
     target
     end))


let uu___is_MoveNode : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| MoveNode (target, new_parent) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MoveNode__item__target : op  ->  Prims.string = (fun ( projectee  :  op ) -> (match (projectee) with
| MoveNode (target, new_parent) -> begin
     target
     end))


let __proj__MoveNode__item__new_parent : op  ->  Prims.string = (fun ( projectee  :  op ) -> (match (projectee) with
| MoveNode (target, new_parent) -> begin
     new_parent
     end))


let uu___is_ReorderChildren : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| ReorderChildren (parent, order) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ReorderChildren__item__parent : op  ->  Prims.string = (fun ( projectee  :  op ) -> (match (projectee) with
| ReorderChildren (parent, order) -> begin
     parent
     end))


let __proj__ReorderChildren__item__order : op  ->  Prims.list<Prims.string> = (fun ( projectee  :  op ) -> (match (projectee) with
| ReorderChildren (parent, order) -> begin
     order
     end))


let uu___is_Batch : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| Batch (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Batch__item___0 : op  ->  Prims.list<op> = (fun ( projectee  :  op ) -> (match (projectee) with
| Batch (_0) -> begin
     _0
     end))


let rec ins : Prims.string  ->  tree  ->  tree  ->  tree = (fun ( p  :  Prims.string ) ( n  :  tree ) ( t  :  tree ) -> (match (t) with
| TNode (i, k, cs) -> begin
     (

let cs' = (ins_all p n cs)
in  
if (Prims.op_Equals i p) then begin
     TNode (i, k, (DagFold.app cs' ((n)::[])))
     end else begin
     TNode (i, k, cs')
     end)
     end))
and ins_all : Prims.string  ->  tree  ->  Prims.list<tree>  ->  Prims.list<tree> = (fun ( p  :  Prims.string ) ( n  :  tree ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::r -> begin
     ((ins p n t))::(ins_all p n r)
     end))


let rec drop_kid : Prims.string  ->  Prims.list<tree>  ->  Prims.list<tree> = (fun ( x  :  Prims.string ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::r -> begin
      
if (Prims.op_Equals (tid_of t) x) then begin
     (drop_kid x r)
     end else begin
     (t)::(drop_kid x r)
     end
     end))


let rec rem_at : Prims.string  ->  Prims.string  ->  tree  ->  tree = (fun ( pid  :  Prims.string ) ( x  :  Prims.string ) ( t  :  tree ) -> (match (t) with
| TNode (i, k, cs) -> begin
     (

let cs' = (rem_all pid x cs)
in  
if (Prims.op_Equals i pid) then begin
     TNode (i, k, (drop_kid x cs'))
     end else begin
     TNode (i, k, cs')
     end)
     end))
and rem_all : Prims.string  ->  Prims.string  ->  Prims.list<tree>  ->  Prims.list<tree> = (fun ( pid  :  Prims.string ) ( x  :  Prims.string ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::r -> begin
     ((rem_at pid x t))::(rem_all pid x r)
     end))


let rec pick_last : Prims.string  ->  Prims.list<tree>  ->  FStar_Pervasives_Native.option<tree> = (fun ( x  :  Prims.string ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (t)::r -> begin
     (match ((pick_last x r)) with
| FStar_Pervasives_Native.Some (n) -> begin
     FStar_Pervasives_Native.Some (n)
     end
| FStar_Pervasives_Native.None -> begin
      
if (Prims.op_Equals (tid_of t) x) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     FStar_Pervasives_Native.None
     end
     end)
     end))


let rec arrange : Prims.list<Prims.string>  ->  Prims.list<tree>  ->  Prims.list<tree> = (fun ( order  :  Prims.list<Prims.string> ) ( ts  :  Prims.list<tree> ) -> (match (order) with
| [] -> begin
     []
     end
| (x)::rest -> begin
     (match ((pick_last x ts)) with
| FStar_Pervasives_Native.Some (n) -> begin
     (n)::(arrange rest ts)
     end
| FStar_Pervasives_Native.None -> begin
     (arrange rest ts)
     end)
     end))


let rec reorder_at : Prims.string  ->  Prims.list<Prims.string>  ->  tree  ->  tree = (fun ( p  :  Prims.string ) ( order  :  Prims.list<Prims.string> ) ( t  :  tree ) -> (match (t) with
| TNode (i, k, cs) -> begin
     (

let cs' = (reorder_all p order cs)
in  
if (Prims.op_Equals i p) then begin
     TNode (i, k, (arrange order cs'))
     end else begin
     TNode (i, k, cs')
     end)
     end))
and reorder_all : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<tree>  ->  Prims.list<tree> = (fun ( p  :  Prims.string ) ( order  :  Prims.list<Prims.string> ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::r -> begin
     ((reorder_at p order t))::(reorder_all p order r)
     end))


let rec remove_first : Prims.string  ->  Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>> = (fun ( x  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (h)::t -> begin
      
if (Prims.op_Equals h x) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     (match ((remove_first x t)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (t') -> begin
     FStar_Pervasives_Native.Some ((h)::t')
     end)
     end
     end))


let rec same_multiset : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( xs  :  Prims.list<Prims.string> ) ( ys  :  Prims.list<Prims.string> ) -> (match (xs) with
| [] -> begin
     (DagFold.is_empty ys)
     end
| (x)::r -> begin
     (match ((remove_first x ys)) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (ys') -> begin
     (same_multiset r ys')
     end)
     end))


let rec apply : op  ->  tree  ->  DagFold.outcome<tree, rejection> = (fun ( o  :  op ) ( t  :  tree ) -> (match (o) with
| InsertChild (p, n) -> begin
      
if (has_id (tid_of n) t) then begin
     DagFold.Error (DuplicateId ((tid_of n)))
     end else begin
      
if (not ((has_id p t))) then begin
     DagFold.Error (UnknownNode (p, (ids t)))
     end else begin
     DagFold.Ok ((ins p n t))
     end
     end
     end
| RemoveNode (x) -> begin
      
if (Prims.op_Equals (tid_of t) x) then begin
     DagFold.Error (CannotRemoveRoot)
     end else begin
      
if (not ((has_id x t))) then begin
     DagFold.Error (UnknownNode (x, (ids t)))
     end else begin
     (match ((parent_of x t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (UnknownNode (x, (ids t)))
     end
| FStar_Pervasives_Native.Some (pid) -> begin
     DagFold.Ok ((rem_at pid x t))
     end)
     end
     end
     end
| ReorderChildren (p, order) -> begin
     (match ((find_in p t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (UnknownNode (p, (ids t)))
     end
| FStar_Pervasives_Native.Some (n) -> begin
     (

let current = (kid_ids (kids_of n))
in  
if (not ((same_multiset current order))) then begin
     DagFold.Error (ReorderMismatch (p, current, order))
     end else begin
     DagFold.Ok ((reorder_at p order t))
     end)
     end)
     end
| MoveNode (x, np) -> begin
      
if (Prims.op_Equals (tid_of t) x) then begin
     DagFold.Error (CannotRemoveRoot)
     end else begin
      
if (not ((has_id x t))) then begin
     DagFold.Error (UnknownNode (x, (ids t)))
     end else begin
      
if (not ((has_id np t))) then begin
     DagFold.Error (UnknownNode (np, (ids t)))
     end else begin
     (match ((find_in x t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (UnknownNode (x, (ids t)))
     end
| FStar_Pervasives_Native.Some (sub) -> begin
      
if (DagFold.mem np (ids sub)) then begin
     DagFold.Error (WouldNestUnderSelf (x))
     end else begin
     (match ((parent_of x t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (UnknownNode (x, (ids t)))
     end
| FStar_Pervasives_Native.Some (pid) -> begin
     (

let removed = (rem_at pid x t)
in  
if (not ((has_id np removed))) then begin
     DagFold.Error (UnknownNode (np, (ids removed)))
     end else begin
     DagFold.Ok ((ins np sub removed))
     end)
     end)
     end
     end)
     end
     end
     end
     end
| Batch (os) -> begin
     (apply_all os t)
     end))
and apply_all : Prims.list<op>  ->  tree  ->  DagFold.outcome<tree, rejection> = (fun ( os  :  Prims.list<op> ) ( t  :  tree ) -> (match (os) with
| [] -> begin
     DagFold.Ok (t)
     end
| (o)::r -> begin
     (match ((apply o t)) with
| DagFold.Ok (t') -> begin
     (apply_all r t')
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end))


let empty_fp : DagFold.footprint = {DagFold.reads = []; DagFold.structure_writes = []; DagFold.content_writes = []; DagFold.unknown_parent_writes = []}


let union_fp : DagFold.footprint  ->  DagFold.footprint  ->  DagFold.footprint = (fun ( a  :  DagFold.footprint ) ( b  :  DagFold.footprint ) -> {DagFold.reads = (DagFold.union a.reads b.reads); DagFold.structure_writes = (DagFold.union a.structure_writes b.structure_writes); DagFold.content_writes = (DagFold.union a.content_writes b.content_writes); DagFold.unknown_parent_writes = (DagFold.union a.unknown_parent_writes b.unknown_parent_writes)})


let rec op_fp : op  ->  DagFold.footprint = (fun ( o  :  op ) -> (match (o) with
| InsertChild (p, n) -> begin
     (

let inserted = (ids n)
in {DagFold.reads = (p)::inserted; DagFold.structure_writes = (p)::[]; DagFold.content_writes = inserted; DagFold.unknown_parent_writes = []})
     end
| RemoveNode (x) -> begin
     {DagFold.reads = (x)::[]; DagFold.structure_writes = []; DagFold.content_writes = (x)::[]; DagFold.unknown_parent_writes = (x)::[]}
     end
| MoveNode (x, np) -> begin
     {DagFold.reads = (x)::(np)::[]; DagFold.structure_writes = (np)::[]; DagFold.content_writes = (x)::[]; DagFold.unknown_parent_writes = (x)::[]}
     end
| ReorderChildren (p, order) -> begin
     {DagFold.reads = (p)::order; DagFold.structure_writes = (p)::[]; DagFold.content_writes = []; DagFold.unknown_parent_writes = []}
     end
| Batch (inner) -> begin
     (fp_all inner)
     end))
and fp_all : Prims.list<op>  ->  DagFold.footprint = (fun ( os  :  Prims.list<op> ) -> (match (os) with
| [] -> begin
     empty_fp
     end
| (o)::r -> begin
     (union_fp (op_fp o) (fp_all r))
     end))


let rec inert : op  ->  Prims.bool = (fun ( o  :  op ) -> (match (o) with
| Batch (os) -> begin
     (inert_all os)
     end
| uu___ -> begin
     false
     end))
and inert_all : Prims.list<op>  ->  Prims.bool = (fun ( os  :  Prims.list<op> ) -> (match (os) with
| [] -> begin
     true
     end
| (o)::r -> begin
     ((inert o) && (inert_all r))
     end))


let relocating : op  ->  Prims.bool = (fun ( o  :  op ) -> (not ((DagFold.is_empty (op_fp o).unknown_parent_writes))))


let rec no_dups : Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::r -> begin
     ((not ((DagFold.mem x r))) && (no_dups r))
     end))


let rec locate : Prims.string  ->  Prims.list<tree>  ->  FStar_Pervasives_Native.option<tree> = (fun ( x  :  Prims.string ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (t)::r -> begin
      
if (DagFold.mem x (ids t)) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     (locate x r)
     end
     end))


let rec reorder_ok : Prims.string  ->  Prims.list<Prims.string>  ->  tree  ->  Prims.bool = (fun ( p  :  Prims.string ) ( o  :  Prims.list<Prims.string> ) ( t  :  tree ) -> (match (t) with
| TNode (i, uu___, cs) -> begin
     (( 
if (Prims.op_Equals i p) then begin
     (same_multiset (kid_ids cs) o)
     end else begin
     true
     end) && (reorder_ok_all p o cs))
     end))
and reorder_ok_all : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<tree>  ->  Prims.bool = (fun ( p  :  Prims.string ) ( o  :  Prims.list<Prims.string> ) ( ts  :  Prims.list<tree> ) -> (match (ts) with
| [] -> begin
     true
     end
| (t)::r -> begin
     ((reorder_ok p o t) && (reorder_ok_all p o r))
     end))


let is_leaf : op  ->  Prims.bool = (fun ( o  :  op ) -> (match (o) with
| Batch (uu___) -> begin
     false
     end
| uu___ -> begin
     true
     end))


let cx_tree : tree = TNode ("root", "doc", (TNode ("a", "section", []))::[])


let cx_insert : op = InsertChild ("a", TNode ("fresh", "section", (TNode ("root", "para", []))::[]))


let covered : op  ->  op  ->  Prims.bool = (fun ( a  :  op ) ( b  :  op ) -> ((((((is_leaf a) && (is_leaf b)) || (inert a)) || (inert b)) || (relocating a)) || (relocating b)))


let wapply : op  ->  tree  ->  DagFold.outcome<tree, rejection> = (fun ( o  :  op ) ( t  :  tree ) ->  
if (not ((wf t))) then begin
     DagFold.Error (Rejected ("state-not-id-unique", "the tree carries an id twice"))
     end else begin
     (match ((apply o t)) with
| DagFold.Ok (t') -> begin
      
if (wf t') then begin
     DagFold.Ok (t')
     end else begin
     DagFold.Error (Rejected ("would-duplicate-an-id", "the inserted subtree carries an id the tree already holds"))
     end
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end)


type leaf_op = op


let leaf_fp : leaf_op  ->  DagFold.footprint = (fun ( o  :  leaf_op ) -> (op_fp o))




