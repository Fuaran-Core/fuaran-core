module TreeDiff
type diff_error =
| RootIdMismatch of Prims.string * Prims.string
| DuplicateIdInTree of Prims.string
| TargetNotAContainer of Prims.string * Prims.string


let uu___is_RootIdMismatch : diff_error  ->  Prims.bool = (fun ( projectee  :  diff_error ) -> (match (projectee) with
| RootIdMismatch (before, after) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RootIdMismatch__item__before : diff_error  ->  Prims.string = (fun ( projectee  :  diff_error ) -> (match (projectee) with
| RootIdMismatch (before, after) -> begin
     before
     end))


let __proj__RootIdMismatch__item__after : diff_error  ->  Prims.string = (fun ( projectee  :  diff_error ) -> (match (projectee) with
| RootIdMismatch (before, after) -> begin
     after
     end))


let uu___is_DuplicateIdInTree : diff_error  ->  Prims.bool = (fun ( projectee  :  diff_error ) -> (match (projectee) with
| DuplicateIdInTree (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateIdInTree__item___0 : diff_error  ->  Prims.string = (fun ( projectee  :  diff_error ) -> (match (projectee) with
| DuplicateIdInTree (_0) -> begin
     _0
     end))


let uu___is_TargetNotAContainer : diff_error  ->  Prims.bool = (fun ( projectee  :  diff_error ) -> (match (projectee) with
| TargetNotAContainer (target, kind_tag) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TargetNotAContainer__item__target : diff_error  ->  Prims.string = (fun ( projectee  :  diff_error ) -> (match (projectee) with
| TargetNotAContainer (target, kind_tag) -> begin
     target
     end))


let __proj__TargetNotAContainer__item__kind_tag : diff_error  ->  Prims.string = (fun ( projectee  :  diff_error ) -> (match (projectee) with
| TargetNotAContainer (target, kind_tag) -> begin
     kind_tag
     end))


let rec pre : TreeOps.tree  ->  Prims.list<TreeOps.tree> = (fun ( t  :  TreeOps.tree ) -> (match (t) with
| TreeOps.TNode (uu___, uu___1, cs) -> begin
     (t)::(pre_all cs)
     end))
and pre_all : Prims.list<TreeOps.tree>  ->  Prims.list<TreeOps.tree> = (fun ( ts  :  Prims.list<TreeOps.tree> ) -> (match (ts) with
| [] -> begin
     []
     end
| (x)::r -> begin
     (DagFold.app (pre x) (pre_all r))
     end))


let rec tids : Prims.list<TreeOps.tree>  ->  Prims.list<Prims.string> = (fun ( ts  :  Prims.list<TreeOps.tree> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::r -> begin
     ((TreeOps.tid_of t))::(tids r)
     end))


let rec lookup : Prims.string  ->  Prims.list<(Prims.string * Prims.string)>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( k  :  Prims.string ) ( l  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((a, b))::r -> begin
     (match ((lookup k r)) with
| FStar_Pervasives_Native.Some (v) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| FStar_Pervasives_Native.None -> begin
      
if (Prims.op_Equals a k) then begin
     FStar_Pervasives_Native.Some (b)
     end else begin
     FStar_Pervasives_Native.None
     end
     end)
     end))


let rec lookup_kids : Prims.string  ->  Prims.list<(Prims.string * Prims.list<Prims.string>)>  ->  FStar_Pervasives_Native.option<Prims.list<Prims.string>> = (fun ( k  :  Prims.string ) ( l  :  Prims.list<(Prims.string * Prims.list<Prims.string>)> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((a, b))::r -> begin
     (match ((lookup_kids k r)) with
| FStar_Pervasives_Native.Some (v) -> begin
     FStar_Pervasives_Native.Some (v)
     end
| FStar_Pervasives_Native.None -> begin
      
if (Prims.op_Equals a k) then begin
     FStar_Pervasives_Native.Some (b)
     end else begin
     FStar_Pervasives_Native.None
     end
     end)
     end))


let rec kid_pairs : Prims.string  ->  Prims.list<TreeOps.tree>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( p  :  Prims.string ) ( cs  :  Prims.list<TreeOps.tree> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
     ((((TreeOps.tid_of c)), (p)))::(kid_pairs p r)
     end))


let rec parent_map : Prims.list<TreeOps.tree>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( ns  :  Prims.list<TreeOps.tree> ) -> (match (ns) with
| [] -> begin
     []
     end
| (p)::r -> begin
     (DagFold.app (kid_pairs (TreeOps.tid_of p) (TreeOps.kids_of p)) (parent_map r))
     end))


let rec kid_map : Prims.list<TreeOps.tree>  ->  Prims.list<(Prims.string * Prims.list<Prims.string>)> = (fun ( ns  :  Prims.list<TreeOps.tree> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::r -> begin
     ((((TreeOps.tid_of n)), ((TreeOps.kid_ids (TreeOps.kids_of n)))))::(kid_map r)
     end))


let dup_id : TreeOps.tree  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( t  :  TreeOps.tree ) -> (TreeOps.scan_dup [] (TreeOps.ids t)))


let shell : TreeOps.tree  ->  TreeOps.tree = (fun ( n  :  TreeOps.tree ) -> TreeOps.TNode ((TreeOps.tid_of n), (TreeOps.kind_of n), []))


let more_than_one = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     false
     end
| (uu___)::[] -> begin
     false
     end
| uu___ -> begin
     true
     end))


let rec pass_inserts : Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<Prims.string>  ->  Prims.list<TreeOps.tree>  ->  Prims.list<TreeOps.op> = (fun ( a_par  :  Prims.list<(Prims.string * Prims.string)> ) ( b_ids  :  Prims.list<Prims.string> ) ( ns  :  Prims.list<TreeOps.tree> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::r -> begin
     (

let k = (TreeOps.tid_of n)
in  
if (DagFold.mem k b_ids) then begin
     (pass_inserts a_par b_ids r)
     end else begin
     (match ((lookup k a_par)) with
| FStar_Pervasives_Native.Some (pid) -> begin
     (TreeOps.InsertChild (pid, (shell n)))::(pass_inserts a_par b_ids r)
     end
| FStar_Pervasives_Native.None -> begin
     (pass_inserts a_par b_ids r)
     end)
     end)
     end))


let rec moves_under : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<TreeOps.tree>  ->  Prims.list<TreeOps.op> = (fun ( pk  :  Prims.string ) ( b_ids  :  Prims.list<Prims.string> ) ( b_par  :  Prims.list<(Prims.string * Prims.string)> ) ( cs  :  Prims.list<TreeOps.tree> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
     (

let ck = (TreeOps.tid_of c)
in (

let moved = ((DagFold.mem ck b_ids) && (match ((lookup ck b_par)) with
| FStar_Pervasives_Native.Some (bp) -> begin
     (Prims.op_Less_Greater bp pk)
     end
| FStar_Pervasives_Native.None -> begin
     true
     end))
in  
if moved then begin
     (TreeOps.MoveNode (ck, pk))::(moves_under pk b_ids b_par r)
     end else begin
     (moves_under pk b_ids b_par r)
     end))
     end))


let rec pass_moves : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<(Prims.string * Prims.list<Prims.string>)>  ->  Prims.list<TreeOps.tree>  ->  (Prims.list<TreeOps.op> * Prims.list<Prims.string>) = (fun ( b_ids  :  Prims.list<Prims.string> ) ( b_par  :  Prims.list<(Prims.string * Prims.string)> ) ( b_kids  :  Prims.list<(Prims.string * Prims.list<Prims.string>)> ) ( ns  :  Prims.list<TreeOps.tree> ) -> (match (ns) with
| [] -> begin
     (([]), ([]))
     end
| (p)::r -> begin
     (

let pk = (TreeOps.tid_of p)
in (

let a_kid_keys = (TreeOps.kid_ids (TreeOps.kids_of p))
in (

let unchanged = ((DagFold.mem pk b_ids) && (match ((lookup_kids pk b_kids)) with
| FStar_Pervasives_Native.Some (bk) -> begin
     (Prims.op_Equals bk a_kid_keys)
     end
| FStar_Pervasives_Native.None -> begin
     false
     end))
in (

let rest = (pass_moves b_ids b_par b_kids r)
in  
if unchanged then begin
     rest
     end else begin
     (((DagFold.app (moves_under pk b_ids b_par (TreeOps.kids_of p)) (FStar_Pervasives_Native.fst rest))), ((pk)::(FStar_Pervasives_Native.snd rest)))
     end))))
     end))


let rec pass_removes : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<TreeOps.tree>  ->  Prims.list<TreeOps.op> = (fun ( a_ids  :  Prims.list<Prims.string> ) ( b_par  :  Prims.list<(Prims.string * Prims.string)> ) ( ns  :  Prims.list<TreeOps.tree> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::r -> begin
     (

let k = (TreeOps.tid_of n)
in  
if (DagFold.mem k a_ids) then begin
     (pass_removes a_ids b_par r)
     end else begin
     (match ((lookup k b_par)) with
| FStar_Pervasives_Native.Some (pid) -> begin
      
if (DagFold.mem pid a_ids) then begin
     (TreeOps.RemoveNode (k))::(pass_removes a_ids b_par r)
     end else begin
     (pass_removes a_ids b_par r)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (pass_removes a_ids b_par r)
     end)
     end)
     end))


let rec pass_reorders : TreeOps.tree  ->  Prims.list<Prims.string>  ->  Prims.list<TreeOps.op> = (fun ( after  :  TreeOps.tree ) ( ps  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     []
     end
| (pid)::r -> begin
     (match ((TreeOps.find_in pid after)) with
| FStar_Pervasives_Native.Some (p) -> begin
      
if (more_than_one (TreeOps.kids_of p)) then begin
     (TreeOps.ReorderChildren (pid, (TreeOps.kid_ids (TreeOps.kids_of p))))::(pass_reorders after r)
     end else begin
     (pass_reorders after r)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (pass_reorders after r)
     end)
     end))


let diff_blocks : TreeOps.tree  ->  TreeOps.tree  ->  (Prims.list<TreeOps.op> * Prims.list<TreeOps.op> * Prims.list<TreeOps.op> * Prims.list<TreeOps.op>) = (fun ( before  :  TreeOps.tree ) ( after  :  TreeOps.tree ) -> (

let b_nodes = (pre before)
in (

let a_nodes = (pre after)
in (

let b_ids = (TreeOps.ids before)
in (

let a_ids = (TreeOps.ids after)
in (

let a_par = (parent_map a_nodes)
in (

let b_par = (parent_map b_nodes)
in (

let b_kids = (kid_map b_nodes)
in (

let p2 = (pass_moves b_ids b_par b_kids a_nodes)
in (((pass_inserts a_par b_ids a_nodes)), ((FStar_Pervasives_Native.fst p2)), ((pass_removes a_ids b_par b_nodes)), ((pass_reorders after (FStar_Pervasives_Native.snd p2)))))))))))))


let script_of : (Prims.list<TreeOps.op> * Prims.list<TreeOps.op> * Prims.list<TreeOps.op> * Prims.list<TreeOps.op>)  ->  Prims.list<TreeOps.op> = (fun ( bl  :  (Prims.list<TreeOps.op> * Prims.list<TreeOps.op> * Prims.list<TreeOps.op> * Prims.list<TreeOps.op>) ) -> (

let uu___ = bl
in (match (uu___) with
| (p1, p2, p3, p4) -> begin
     (DagFold.app p1 (DagFold.app p2 (DagFold.app p3 p4)))
     end)))


let to_ops : TreeOps.tree  ->  TreeOps.tree  ->  DagFold.outcome<Prims.list<TreeOps.op>, diff_error> = (fun ( before  :  TreeOps.tree ) ( after  :  TreeOps.tree ) ->  
if (Prims.op_Less_Greater (TreeOps.tid_of before) (TreeOps.tid_of after)) then begin
     DagFold.Error (RootIdMismatch ((TreeOps.tid_of before), (TreeOps.tid_of after)))
     end else begin
     (match ((dup_id before)) with
| FStar_Pervasives_Native.Some (d) -> begin
     DagFold.Error (DuplicateIdInTree (d))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((dup_id after)) with
| FStar_Pervasives_Native.Some (d) -> begin
     DagFold.Error (DuplicateIdInTree (d))
     end
| FStar_Pervasives_Native.None -> begin
     DagFold.Ok ((script_of (diff_blocks before after)))
     end)
     end)
     end)


let rec first_non_container : (TreeOps.tree  ->  Prims.bool)  ->  Prims.list<TreeOps.tree>  ->  FStar_Pervasives_Native.option<TreeOps.tree> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( ns  :  Prims.list<TreeOps.tree> ) -> (match (ns) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (n)::r -> begin
      
if ((match ((TreeOps.kids_of n)) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) && (not ((ch n)))) then begin
     FStar_Pervasives_Native.Some (n)
     end else begin
     (first_non_container ch r)
     end
     end))


let to_ops_contained : (TreeOps.tree  ->  Prims.bool)  ->  TreeOps.tree  ->  TreeOps.tree  ->  DagFold.outcome<Prims.list<TreeOps.op>, diff_error> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( before  :  TreeOps.tree ) ( after  :  TreeOps.tree ) -> (match ((first_non_container ch (pre after))) with
| FStar_Pervasives_Native.Some (p) -> begin
     DagFold.Error (TargetNotAContainer ((TreeOps.tid_of p), (TreeOps.kind_of p)))
     end
| FStar_Pervasives_Native.None -> begin
     (to_ops before after)
     end))


let is_insert : TreeOps.op  ->  Prims.bool = (fun ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.InsertChild (parent, node) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let is_move : TreeOps.op  ->  Prims.bool = (fun ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.MoveNode (target, new_parent) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let is_remove : TreeOps.op  ->  Prims.bool = (fun ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.RemoveNode (target) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let is_reorder : TreeOps.op  ->  Prims.bool = (fun ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.ReorderChildren (parent, order) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let rec all_ops : (TreeOps.op  ->  Prims.bool)  ->  Prims.list<TreeOps.op>  ->  Prims.bool = (fun ( p  :  TreeOps.op  ->  Prims.bool ) ( l  :  Prims.list<TreeOps.op> ) -> (match (l) with
| [] -> begin
     true
     end
| (o)::r -> begin
     ((p o) && (all_ops p r))
     end))


let insert_shape : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)>  ->  TreeOps.op  ->  Prims.bool = (fun ( b_ids  :  Prims.list<Prims.string> ) ( a_par  :  Prims.list<(Prims.string * Prims.string)> ) ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.InsertChild (p, n) -> begin
     (((match ((TreeOps.kids_of n)) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) && (not ((DagFold.mem (TreeOps.tid_of n) b_ids)))) && (Prims.op_Equals (lookup (TreeOps.tid_of n) a_par) (FStar_Pervasives_Native.Some (p))))
     end
| uu___ -> begin
     true
     end))


let rec is_after_child : Prims.list<TreeOps.tree>  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( ns  :  Prims.list<TreeOps.tree> ) ( x  :  Prims.string ) ( np  :  Prims.string ) -> (match (ns) with
| [] -> begin
     false
     end
| (n)::r -> begin
     (((Prims.op_Equals (TreeOps.tid_of n) np) && (DagFold.mem x (TreeOps.kid_ids (TreeOps.kids_of n)))) || (is_after_child r x np))
     end))


let move_shape : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<TreeOps.tree>  ->  TreeOps.op  ->  Prims.bool = (fun ( b_ids  :  Prims.list<Prims.string> ) ( b_par  :  Prims.list<(Prims.string * Prims.string)> ) ( ns  :  Prims.list<TreeOps.tree> ) ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.MoveNode (x, np) -> begin
     (((DagFold.mem x b_ids) && (match ((lookup x b_par)) with
| FStar_Pervasives_Native.Some (bp) -> begin
     (Prims.op_Less_Greater bp np)
     end
| FStar_Pervasives_Native.None -> begin
     true
     end)) && (is_after_child ns x np))
     end
| uu___ -> begin
     true
     end))


let remove_shape : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)>  ->  TreeOps.op  ->  Prims.bool = (fun ( a_ids  :  Prims.list<Prims.string> ) ( b_par  :  Prims.list<(Prims.string * Prims.string)> ) ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.RemoveNode (x) -> begin
     ((not ((DagFold.mem x a_ids))) && (match ((lookup x b_par)) with
| FStar_Pervasives_Native.Some (pid) -> begin
     (DagFold.mem pid a_ids)
     end
| FStar_Pervasives_Native.None -> begin
     false
     end))
     end
| uu___ -> begin
     true
     end))


let reorder_shape : TreeOps.tree  ->  TreeOps.op  ->  Prims.bool = (fun ( after  :  TreeOps.tree ) ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.ReorderChildren (p, ord) -> begin
     (match ((TreeOps.find_in p after)) with
| FStar_Pervasives_Native.Some (pn) -> begin
     ((Prims.op_Equals ord (TreeOps.kid_ids (TreeOps.kids_of pn))) && (more_than_one (TreeOps.kids_of pn)))
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)
     end
| uu___ -> begin
     true
     end))


let script_shape : TreeOps.tree  ->  TreeOps.tree  ->  TreeOps.op  ->  Prims.bool = (fun ( b  :  TreeOps.tree ) ( a  :  TreeOps.tree ) ( o  :  TreeOps.op ) -> ((((insert_shape (TreeOps.ids b) (parent_map (pre a)) o) && (move_shape (TreeOps.ids b) (parent_map (pre b)) (pre a) o)) && (remove_shape (TreeOps.ids a) (parent_map (pre b)) o)) && (reorder_shape a o)))


let rec parent_accepts : (TreeOps.tree  ->  Prims.bool)  ->  Prims.list<TreeOps.tree>  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( ns  :  Prims.list<TreeOps.tree> ) ( x  :  Prims.string ) ( np  :  Prims.string ) -> (match (ns) with
| [] -> begin
     false
     end
| (n)::r -> begin
     ((((Prims.op_Equals (TreeOps.tid_of n) np) && (DagFold.mem x (TreeOps.kid_ids (TreeOps.kids_of n)))) && (ch n)) || (parent_accepts ch r x np))
     end))


let contained_shape : (TreeOps.tree  ->  Prims.bool)  ->  Prims.list<TreeOps.tree>  ->  TreeOps.op  ->  Prims.bool = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( ns  :  Prims.list<TreeOps.tree> ) ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.InsertChild (p, n) -> begin
     (parent_accepts ch ns (TreeOps.tid_of n) p)
     end
| TreeOps.MoveNode (x, np) -> begin
     (parent_accepts ch ns x np)
     end
| uu___ -> begin
     true
     end))


let pos_before : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("p", "para", []))::[])


let pos_after : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("q", "sec", (TreeOps.TNode ("p", "para", []))::[]))::[])


let rec kid_holder : Prims.list<TreeOps.tree>  ->  Prims.string  ->  FStar_Pervasives_Native.option<TreeOps.tree> = (fun ( ns  :  Prims.list<TreeOps.tree> ) ( x  :  Prims.string ) -> (match (ns) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (n)::r -> begin
      
if (DagFold.mem x (TreeOps.kid_ids (TreeOps.kids_of n))) then begin
     FStar_Pervasives_Native.Some (n)
     end else begin
     (kid_holder r x)
     end
     end))


let rec node_with : Prims.list<TreeOps.tree>  ->  Prims.string  ->  FStar_Pervasives_Native.option<TreeOps.tree> = (fun ( ns  :  Prims.list<TreeOps.tree> ) ( k  :  Prims.string ) -> (match (ns) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (n)::r -> begin
      
if (Prims.op_Equals (TreeOps.tid_of n) k) then begin
     FStar_Pervasives_Native.Some (n)
     end else begin
     (node_with r k)
     end
     end))


let side : TreeOps.tree  ->  TreeOps.tree  ->  Prims.bool  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( b  :  TreeOps.tree ) ( a  :  TreeOps.tree ) ( placed  :  Prims.bool ) ( q  :  Prims.string ) ( c  :  Prims.string ) ->  
if placed then begin
     (DagFold.mem c (Preservation.kids_at q a))
     end else begin
     (DagFold.mem c (Preservation.kids_at q b))
     end)


let kind_src : TreeOps.tree  ->  TreeOps.tree  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( b  :  TreeOps.tree ) ( a  :  TreeOps.tree ) ( q  :  Prims.string ) ->  
if (DagFold.mem q (TreeOps.ids b)) then begin
     (Preservation.kind_at q b)
     end else begin
     (Preservation.kind_at q a)
     end)


let same_kids : TreeOps.tree  ->  TreeOps.tree  ->  Prims.string  ->  Prims.bool = (fun ( b  :  TreeOps.tree ) ( a  :  TreeOps.tree ) ( q  :  Prims.string ) -> (((DagFold.mem q (TreeOps.ids a)) && (DagFold.mem q (TreeOps.ids b))) && (Prims.op_Equals (Preservation.kids_at q b) (Preservation.kids_at q a))))


let cond1 : TreeOps.tree  ->  TreeOps.tree  ->  Prims.list<TreeOps.tree>  ->  Prims.string  ->  Prims.bool = (fun ( b  :  TreeOps.tree ) ( a  :  TreeOps.tree ) ( ns  :  Prims.list<TreeOps.tree> ) ( c  :  Prims.string ) -> (((DagFold.mem c (TreeOps.ids a)) && (not ((DagFold.mem c (TreeOps.ids b))))) && (not ((DagFold.mem c (tids ns))))))


let cond2 : TreeOps.tree  ->  TreeOps.tree  ->  Prims.list<Prims.string>  ->  Prims.list<TreeOps.tree>  ->  Prims.string  ->  Prims.bool = (fun ( b  :  TreeOps.tree ) ( a  :  TreeOps.tree ) ( rest  :  Prims.list<Prims.string> ) ( ns  :  Prims.list<TreeOps.tree> ) ( c  :  Prims.string ) -> ((DagFold.mem c (TreeOps.ids a)) && ((not ((DagFold.mem c (TreeOps.ids b)))) || (not (((DagFold.mem c rest) || (match ((kid_holder ns c)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end)))))))


let outside_sfx : TreeOps.tree  ->  Prims.list<TreeOps.tree>  ->  Prims.string  ->  Prims.bool = (fun ( a  :  TreeOps.tree ) ( sfx  :  Prims.list<TreeOps.tree> ) ( y  :  Prims.string ) -> ((DagFold.mem y (TreeOps.ids a)) && (match ((kid_holder sfx y)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end)))


let top : TreeOps.tree  ->  TreeOps.tree  ->  Prims.string  ->  Prims.bool = (fun ( b  :  TreeOps.tree ) ( a  :  TreeOps.tree ) ( y  :  Prims.string ) -> ((not ((DagFold.mem y (TreeOps.ids a)))) && (match ((lookup y (parent_map (pre b)))) with
| FStar_Pervasives_Native.Some (pid) -> begin
     (DagFold.mem pid (TreeOps.ids a))
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)))


let side3 : TreeOps.tree  ->  TreeOps.tree  ->  Prims.list<TreeOps.tree>  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( b  :  TreeOps.tree ) ( a  :  TreeOps.tree ) ( ns  :  Prims.list<TreeOps.tree> ) ( q  :  Prims.string ) ( c  :  Prims.string ) ->  
if (DagFold.mem c (TreeOps.ids a)) then begin
     (DagFold.mem c (Preservation.kids_at q a))
     end else begin
     ((DagFold.mem c (Preservation.kids_at q b)) && ((not ((DagFold.mem q (TreeOps.ids a)))) || (DagFold.mem c (tids ns))))
     end)


let rec_before : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("x", "sec", (TreeOps.TNode ("p", "para", []))::[]))::(TreeOps.TNode ("y", "para", []))::[])


let rec_after : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("q", "sec", (TreeOps.TNode ("p", "para", []))::[]))::(TreeOps.TNode ("y", "para", []))::[])


let kind_before : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("x", "sec", []))::[])


let kind_after : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("x", "para", []))::[])




