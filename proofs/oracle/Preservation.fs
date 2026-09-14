module Preservation

let rec raisable : TreeOps.op  ->  TreeOps.rejection  ->  Prims.bool = (fun ( o  :  TreeOps.op ) ( e  :  TreeOps.rejection ) -> (match (((o), (e))) with
| (TreeOps.InsertChild (uu___, uu___1), TreeOps.DuplicateId (uu___2)) -> begin
     true
     end
| (TreeOps.InsertChild (uu___, uu___1), TreeOps.UnknownNode (uu___2, uu___3)) -> begin
     true
     end
| (TreeOps.RemoveNode (uu___), TreeOps.CannotRemoveRoot) -> begin
     true
     end
| (TreeOps.RemoveNode (uu___), TreeOps.UnknownNode (uu___1, uu___2)) -> begin
     true
     end
| (TreeOps.ReorderChildren (uu___, uu___1), TreeOps.UnknownNode (uu___2, uu___3)) -> begin
     true
     end
| (TreeOps.ReorderChildren (uu___, uu___1), TreeOps.ReorderMismatch (uu___2, uu___3, uu___4)) -> begin
     true
     end
| (TreeOps.MoveNode (uu___, uu___1), TreeOps.CannotRemoveRoot) -> begin
     true
     end
| (TreeOps.MoveNode (uu___, uu___1), TreeOps.UnknownNode (uu___2, uu___3)) -> begin
     true
     end
| (TreeOps.MoveNode (uu___, uu___1), TreeOps.WouldNestUnderSelf (uu___2)) -> begin
     true
     end
| (TreeOps.Batch (os), uu___) -> begin
     (raisable_all os e)
     end
| (uu___, uu___1) -> begin
     false
     end))
and raisable_all : Prims.list<TreeOps.op>  ->  TreeOps.rejection  ->  Prims.bool = (fun ( os  :  Prims.list<TreeOps.op> ) ( e  :  TreeOps.rejection ) -> (match (os) with
| [] -> begin
     false
     end
| (o)::r -> begin
     ((raisable o e) || (raisable_all r e))
     end))


let state_after : TreeOps.op  ->  TreeOps.tree  ->  TreeOps.tree = (fun ( o  :  TreeOps.op ) ( t  :  TreeOps.tree ) -> (match ((TreeOps.apply o t)) with
| DagFold.Ok (t') -> begin
     t'
     end
| DagFold.Error (uu___) -> begin
     t
     end))


let rec kid_with : Prims.string  ->  Prims.list<TreeOps.tree>  ->  FStar_Pervasives_Native.option<TreeOps.tree> = (fun ( x  :  Prims.string ) ( ts  :  Prims.list<TreeOps.tree> ) -> (match (ts) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (t)::r -> begin
      
if (Prims.op_Equals (TreeOps.tid_of t) x) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     (kid_with x r)
     end
     end))


let can_apply : TreeOps.op  ->  TreeOps.tree  ->  DagFold.outcome<unit, TreeOps.rejection> = (fun ( o  :  TreeOps.op ) ( t  :  TreeOps.tree ) -> (match (o) with
| TreeOps.InsertChild (p, n) -> begin
     (match ((TreeOps.first_dup n t)) with
| FStar_Pervasives_Native.Some (d) -> begin
     DagFold.Error (TreeOps.DuplicateId (d))
     end
| FStar_Pervasives_Native.None -> begin
      
if (not ((TreeOps.has_id p t))) then begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end else begin
     DagFold.Ok (())
     end
     end)
     end
| TreeOps.RemoveNode (x) -> begin
      
if (Prims.op_Equals (TreeOps.tid_of t) x) then begin
     DagFold.Error (TreeOps.CannotRemoveRoot)
     end else begin
      
if (not ((TreeOps.has_id x t))) then begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids t)))
     end else begin
     DagFold.Ok (())
     end
     end
     end
| TreeOps.ReorderChildren (p, order) -> begin
     (match ((TreeOps.find_in p t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (n) -> begin
     (

let current = (TreeOps.kid_ids (TreeOps.kids_of n))
in  
if (not ((TreeOps.same_multiset current order))) then begin
     DagFold.Error (TreeOps.ReorderMismatch (p, current, order))
     end else begin
     DagFold.Ok (())
     end)
     end)
     end
| TreeOps.MoveNode (uu___, uu___1) -> begin
     (match ((TreeOps.apply o t)) with
| DagFold.Ok (uu___2) -> begin
     DagFold.Ok (())
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end
| TreeOps.Batch (uu___) -> begin
     (match ((TreeOps.apply o t)) with
| DagFold.Ok (uu___1) -> begin
     DagFold.Ok (())
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end))


let order_in : TreeOps.tree  ->  Prims.list<Prims.string> = (fun ( pnode  :  TreeOps.tree ) -> (TreeOps.kid_ids (TreeOps.kids_of pnode)))


let rec last_of : Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::[] -> begin
     FStar_Pervasives_Native.Some (x)
     end
| (uu___)::r -> begin
     (last_of r)
     end))


let restoring : TreeOps.tree  ->  Prims.string  ->  TreeOps.op  ->  TreeOps.op = (fun ( pnode  :  TreeOps.tree ) ( target  :  Prims.string ) ( put  :  TreeOps.op ) -> (

let order = (order_in pnode)
in  
if (Prims.op_Equals (last_of order) (FStar_Pervasives_Native.Some (target))) then begin
     put
     end else begin
     TreeOps.Batch ((put)::(TreeOps.ReorderChildren ((TreeOps.tid_of pnode), order))::[])
     end))


let invert_leaf : TreeOps.leaf_op  ->  TreeOps.tree  ->  DagFold.outcome<TreeOps.op, TreeOps.rejection> = (fun ( o  :  TreeOps.leaf_op ) ( pre  :  TreeOps.tree ) -> (match ((can_apply o pre)) with
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end
| DagFold.Ok (()) -> begin
     (match (o) with
| TreeOps.InsertChild (uu___, n) -> begin
     DagFold.Ok (TreeOps.RemoveNode ((TreeOps.tid_of n)))
     end
| TreeOps.RemoveNode (x) -> begin
     (match ((TreeOps.parent_of x pre)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids pre)))
     end
| FStar_Pervasives_Native.Some (pid) -> begin
     (match ((((TreeOps.find_in pid pre)), ((TreeOps.find_in x pre)))) with
| (FStar_Pervasives_Native.Some (pnode), FStar_Pervasives_Native.Some (sub)) -> begin
     DagFold.Ok ((restoring pnode x (TreeOps.InsertChild (pid, sub))))
     end
| (uu___, uu___1) -> begin
     DagFold.Error (TreeOps.UnknownNode (pid, (TreeOps.ids pre)))
     end)
     end)
     end
| TreeOps.MoveNode (x, uu___) -> begin
     (match ((TreeOps.parent_of x pre)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids pre)))
     end
| FStar_Pervasives_Native.Some (pid) -> begin
     (match ((TreeOps.find_in pid pre)) with
| FStar_Pervasives_Native.Some (pnode) -> begin
     DagFold.Ok ((restoring pnode x (TreeOps.MoveNode (x, pid))))
     end
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (pid, (TreeOps.ids pre)))
     end)
     end)
     end
| TreeOps.ReorderChildren (p, uu___) -> begin
     (match ((TreeOps.find_in p pre)) with
| FStar_Pervasives_Native.Some (pn) -> begin
     DagFold.Ok (TreeOps.ReorderChildren (p, (order_in pn)))
     end
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids pre)))
     end)
     end)
     end))


let rec contained : (TreeOps.tree  ->  Prims.bool)  ->  TreeOps.tree  ->  Prims.bool = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( t  :  TreeOps.tree ) -> (match (t) with
| TreeOps.TNode (uu___, uu___1, cs) -> begin
     ((match (cs) with
| [] -> begin
     true
     end
| uu___2 -> begin
     (ch t)
     end) && (contained_all ch cs))
     end))
and contained_all : (TreeOps.tree  ->  Prims.bool)  ->  Prims.list<TreeOps.tree>  ->  Prims.bool = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( ts  :  Prims.list<TreeOps.tree> ) -> (match (ts) with
| [] -> begin
     true
     end
| (t)::r -> begin
     ((contained ch t) && (contained_all ch r))
     end))


let rec contained_op : (TreeOps.tree  ->  Prims.bool)  ->  TreeOps.op  ->  Prims.bool = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.InsertChild (uu___, n) -> begin
     (contained ch n)
     end
| TreeOps.Batch (os) -> begin
     (contained_op_all ch os)
     end
| uu___ -> begin
     true
     end))
and contained_op_all : (TreeOps.tree  ->  Prims.bool)  ->  Prims.list<TreeOps.op>  ->  Prims.bool = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( os  :  Prims.list<TreeOps.op> ) -> (match (os) with
| [] -> begin
     true
     end
| (o)::r -> begin
     ((contained_op ch o) && (contained_op_all ch r))
     end))


let rec first_uncontained : (TreeOps.tree  ->  Prims.bool)  ->  TreeOps.tree  ->  FStar_Pervasives_Native.option<TreeOps.tree> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( t  :  TreeOps.tree ) -> (match (t) with
| TreeOps.TNode (uu___, uu___1, cs) -> begin
      
if ((match (cs) with
| (hd)::tl -> begin
     true
     end
| uu___2 -> begin
     false
     end) && (not ((ch t)))) then begin
     FStar_Pervasives_Native.Some (t)
     end else begin
     (first_uncontained_all ch cs)
     end
     end))
and first_uncontained_all : (TreeOps.tree  ->  Prims.bool)  ->  Prims.list<TreeOps.tree>  ->  FStar_Pervasives_Native.option<TreeOps.tree> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( ts  :  Prims.list<TreeOps.tree> ) -> (match (ts) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (t)::r -> begin
     (match ((first_uncontained ch t)) with
| FStar_Pervasives_Native.Some (o) -> begin
     FStar_Pervasives_Native.Some (o)
     end
| FStar_Pervasives_Native.None -> begin
     (first_uncontained_all ch r)
     end)
     end))


let rec apply_contained : (TreeOps.tree  ->  Prims.bool)  ->  TreeOps.op  ->  TreeOps.tree  ->  DagFold.outcome<TreeOps.tree, TreeOps.rejection> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( o  :  TreeOps.op ) ( t  :  TreeOps.tree ) -> (match (o) with
| TreeOps.InsertChild (p, n) -> begin
     (match ((TreeOps.first_dup n t)) with
| FStar_Pervasives_Native.Some (d) -> begin
     DagFold.Error (TreeOps.DuplicateId (d))
     end
| FStar_Pervasives_Native.None -> begin
      
if (not ((TreeOps.has_id p t))) then begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end else begin
     (match ((TreeOps.find_in p t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (pn) -> begin
      
if (not ((ch pn))) then begin
     DagFold.Error (TreeOps.NotAContainer (p, (TreeOps.kind_of pn)))
     end else begin
     (match ((first_uncontained ch n)) with
| FStar_Pervasives_Native.Some (off) -> begin
     DagFold.Error (TreeOps.NotAContainer ((TreeOps.tid_of off), (TreeOps.kind_of off)))
     end
| FStar_Pervasives_Native.None -> begin
     DagFold.Ok ((TreeOps.ins p n t))
     end)
     end
     end)
     end
     end)
     end
| TreeOps.RemoveNode (x) -> begin
      
if (Prims.op_Equals (TreeOps.tid_of t) x) then begin
     DagFold.Error (TreeOps.CannotRemoveRoot)
     end else begin
      
if (not ((TreeOps.has_id x t))) then begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids t)))
     end else begin
     (match ((TreeOps.parent_of x t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (pid) -> begin
     DagFold.Ok ((TreeOps.rem_at pid x t))
     end)
     end
     end
     end
| TreeOps.ReorderChildren (p, order) -> begin
     (match ((TreeOps.find_in p t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (n) -> begin
     (

let current = (TreeOps.kid_ids (TreeOps.kids_of n))
in  
if (not ((TreeOps.same_multiset current order))) then begin
     DagFold.Error (TreeOps.ReorderMismatch (p, current, order))
     end else begin
     DagFold.Ok ((TreeOps.reorder_at p order t))
     end)
     end)
     end
| TreeOps.MoveNode (x, np) -> begin
      
if (Prims.op_Equals (TreeOps.tid_of t) x) then begin
     DagFold.Error (TreeOps.CannotRemoveRoot)
     end else begin
      
if (not ((TreeOps.has_id x t))) then begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids t)))
     end else begin
      
if (not ((TreeOps.has_id np t))) then begin
     DagFold.Error (TreeOps.UnknownNode (np, (TreeOps.ids t)))
     end else begin
     (match ((TreeOps.find_in np t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (np, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (np0) -> begin
      
if (not ((ch np0))) then begin
     DagFold.Error (TreeOps.NotAContainer (np, (TreeOps.kind_of np0)))
     end else begin
     (match ((TreeOps.find_in x t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (sub) -> begin
      
if (DagFold.mem np (TreeOps.ids sub)) then begin
     DagFold.Error (TreeOps.WouldNestUnderSelf (x))
     end else begin
     (match ((TreeOps.parent_of x t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (pid) -> begin
     (

let removed = (TreeOps.rem_at pid x t)
in  
if (not ((TreeOps.has_id np removed))) then begin
     DagFold.Error (TreeOps.UnknownNode (np, (TreeOps.ids removed)))
     end else begin
     DagFold.Ok ((TreeOps.ins np sub removed))
     end)
     end)
     end
     end)
     end
     end)
     end
     end
     end
     end
| TreeOps.Batch (os) -> begin
     (apply_contained_all ch os t)
     end))
and apply_contained_all : (TreeOps.tree  ->  Prims.bool)  ->  Prims.list<TreeOps.op>  ->  TreeOps.tree  ->  DagFold.outcome<TreeOps.tree, TreeOps.rejection> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( os  :  Prims.list<TreeOps.op> ) ( t  :  TreeOps.tree ) -> (match (os) with
| [] -> begin
     DagFold.Ok (t)
     end
| (o)::r -> begin
     (match ((apply_contained ch o t)) with
| DagFold.Ok (t') -> begin
     (apply_contained_all ch r t')
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end))


let can_apply_contained : (TreeOps.tree  ->  Prims.bool)  ->  TreeOps.op  ->  TreeOps.tree  ->  DagFold.outcome<unit, TreeOps.rejection> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( o  :  TreeOps.op ) ( t  :  TreeOps.tree ) -> (match (o) with
| TreeOps.InsertChild (p, n) -> begin
     (match ((TreeOps.first_dup n t)) with
| FStar_Pervasives_Native.Some (d) -> begin
     DagFold.Error (TreeOps.DuplicateId (d))
     end
| FStar_Pervasives_Native.None -> begin
      
if (not ((TreeOps.has_id p t))) then begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end else begin
     (match ((TreeOps.find_in p t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (pn) -> begin
      
if (not ((ch pn))) then begin
     DagFold.Error (TreeOps.NotAContainer (p, (TreeOps.kind_of pn)))
     end else begin
     (match ((first_uncontained ch n)) with
| FStar_Pervasives_Native.Some (off) -> begin
     DagFold.Error (TreeOps.NotAContainer ((TreeOps.tid_of off), (TreeOps.kind_of off)))
     end
| FStar_Pervasives_Native.None -> begin
     DagFold.Ok (())
     end)
     end
     end)
     end
     end)
     end
| TreeOps.RemoveNode (x) -> begin
      
if (Prims.op_Equals (TreeOps.tid_of t) x) then begin
     DagFold.Error (TreeOps.CannotRemoveRoot)
     end else begin
      
if (not ((TreeOps.has_id x t))) then begin
     DagFold.Error (TreeOps.UnknownNode (x, (TreeOps.ids t)))
     end else begin
     DagFold.Ok (())
     end
     end
     end
| TreeOps.ReorderChildren (p, order) -> begin
     (match ((TreeOps.find_in p t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (n) -> begin
     (

let current = (TreeOps.kid_ids (TreeOps.kids_of n))
in  
if (not ((TreeOps.same_multiset current order))) then begin
     DagFold.Error (TreeOps.ReorderMismatch (p, current, order))
     end else begin
     DagFold.Ok (())
     end)
     end)
     end
| TreeOps.MoveNode (uu___, uu___1) -> begin
     (match ((apply_contained ch o t)) with
| DagFold.Ok (uu___2) -> begin
     DagFold.Ok (())
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end
| TreeOps.Batch (uu___) -> begin
     (match ((apply_contained ch o t)) with
| DagFold.Ok (uu___1) -> begin
     DagFold.Ok (())
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end))


let rec can_apply_all : Prims.list<TreeOps.op>  ->  TreeOps.tree  ->  DagFold.outcome<unit, TreeOps.rejection> = (fun ( os  :  Prims.list<TreeOps.op> ) ( t  :  TreeOps.tree ) -> (match (os) with
| [] -> begin
     DagFold.Ok (())
     end
| (o)::r -> begin
     (match ((TreeOps.apply o t)) with
| DagFold.Ok (t') -> begin
     (can_apply_all r t')
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end))


let apply_contained_pre161 : (TreeOps.tree  ->  Prims.bool)  ->  TreeOps.op  ->  TreeOps.tree  ->  DagFold.outcome<TreeOps.tree, TreeOps.rejection> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( o  :  TreeOps.op ) ( t  :  TreeOps.tree ) -> (match (o) with
| TreeOps.InsertChild (p, n) -> begin
     (match ((TreeOps.first_dup n t)) with
| FStar_Pervasives_Native.Some (d) -> begin
     DagFold.Error (TreeOps.DuplicateId (d))
     end
| FStar_Pervasives_Native.None -> begin
      
if (not ((TreeOps.has_id p t))) then begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end else begin
     (match ((TreeOps.find_in p t)) with
| FStar_Pervasives_Native.None -> begin
     DagFold.Error (TreeOps.UnknownNode (p, (TreeOps.ids t)))
     end
| FStar_Pervasives_Native.Some (pn) -> begin
      
if (not ((ch pn))) then begin
     DagFold.Error (TreeOps.NotAContainer (p, (TreeOps.kind_of pn)))
     end else begin
     DagFold.Ok ((TreeOps.ins p n t))
     end
     end)
     end
     end)
     end
| uu___ -> begin
     (apply_contained ch o t)
     end))


let rec apply_contained_insert_only : (TreeOps.tree  ->  Prims.bool)  ->  TreeOps.op  ->  TreeOps.tree  ->  DagFold.outcome<TreeOps.tree, TreeOps.rejection> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( o  :  TreeOps.op ) ( t  :  TreeOps.tree ) -> (match (o) with
| TreeOps.InsertChild (uu___, uu___1) -> begin
     (apply_contained ch o t)
     end
| TreeOps.Batch (os) -> begin
     (apply_contained_insert_only_all ch os t)
     end
| uu___ -> begin
     (TreeOps.apply o t)
     end))
and apply_contained_insert_only_all : (TreeOps.tree  ->  Prims.bool)  ->  Prims.list<TreeOps.op>  ->  TreeOps.tree  ->  DagFold.outcome<TreeOps.tree, TreeOps.rejection> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( os  :  Prims.list<TreeOps.op> ) ( t  :  TreeOps.tree ) -> (match (os) with
| [] -> begin
     DagFold.Ok (t)
     end
| (o)::r -> begin
     (match ((apply_contained_insert_only ch o t)) with
| DagFold.Ok (t') -> begin
     (apply_contained_insert_only_all ch r t')
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end))


let rec ch_at : (TreeOps.tree  ->  Prims.bool)  ->  Prims.string  ->  TreeOps.tree  ->  Prims.bool = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( p  :  Prims.string ) ( t  :  TreeOps.tree ) -> (match (t) with
| TreeOps.TNode (i, uu___, cs) -> begin
     (( 
if (Prims.op_Equals i p) then begin
     (ch t)
     end else begin
     true
     end) && (ch_at_all ch p cs))
     end))
and ch_at_all : (TreeOps.tree  ->  Prims.bool)  ->  Prims.string  ->  Prims.list<TreeOps.tree>  ->  Prims.bool = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( p  :  Prims.string ) ( ts  :  Prims.list<TreeOps.tree> ) -> (match (ts) with
| [] -> begin
     true
     end
| (t)::r -> begin
     ((ch_at ch p t) && (ch_at_all ch p r))
     end))


let cx_doc_only : TreeOps.tree  ->  Prims.bool = (fun ( n  :  TreeOps.tree ) -> (Prims.op_Equals (TreeOps.kind_of n) "doc"))


let cx_childless : TreeOps.tree  ->  Prims.bool = (fun ( n  :  TreeOps.tree ) -> (match ((TreeOps.kids_of n)) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end))


let cx_root_doc : TreeOps.tree = TreeOps.TNode ("root", "doc", [])


let cx_nested_graft : TreeOps.op = TreeOps.InsertChild ("root", TreeOps.TNode ("a", "para", (TreeOps.TNode ("b", "para", []))::[]))


let cx_leaf_graft : TreeOps.op = TreeOps.InsertChild ("root", TreeOps.TNode ("a", "para", []))


let cx_box : TreeOps.tree  ->  Prims.bool = (fun ( n  :  TreeOps.tree ) -> (Prims.op_Equals (TreeOps.kind_of n) "box"))


let cx_box_tree : TreeOps.tree = TreeOps.TNode ("root", "box", (TreeOps.TNode ("leaf", "para", []))::(TreeOps.TNode ("x", "para", []))::[])


let cx_move_into_leaf : TreeOps.op = TreeOps.MoveNode ("x", "leaf")


let rec apply_all_with : (TreeOps.tree  ->  Prims.bool)  ->  Prims.nat  ->  Prims.list<TreeOps.op>  ->  TreeOps.tree  ->  DagFold.outcome<TreeOps.tree, (Prims.nat * TreeOps.rejection * TreeOps.tree)> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( i  :  Prims.nat ) ( os  :  Prims.list<TreeOps.op> ) ( t  :  TreeOps.tree ) -> (match (os) with
| [] -> begin
     DagFold.Ok (t)
     end
| (o)::r -> begin
     (match ((apply_contained ch o t)) with
| DagFold.Ok (t') -> begin
     (apply_all_with ch (i + (Prims.parse_int "1")) r t')
     end
| DagFold.Error (e) -> begin
     DagFold.Error (((i), (e), (t)))
     end)
     end))


let rec can_apply_all_with : (TreeOps.tree  ->  Prims.bool)  ->  Prims.nat  ->  Prims.list<TreeOps.op>  ->  TreeOps.tree  ->  DagFold.outcome<unit, (Prims.nat * TreeOps.rejection)> = (fun ( ch  :  TreeOps.tree  ->  Prims.bool ) ( i  :  Prims.nat ) ( os  :  Prims.list<TreeOps.op> ) ( t  :  TreeOps.tree ) -> (match (os) with
| [] -> begin
     DagFold.Ok (())
     end
| (o)::r -> begin
     (match ((apply_contained ch o t)) with
| DagFold.Ok (t') -> begin
     (can_apply_all_with ch (i + (Prims.parse_int "1")) r t')
     end
| DagFold.Error (e) -> begin
     DagFold.Error (((i), (e)))
     end)
     end))




