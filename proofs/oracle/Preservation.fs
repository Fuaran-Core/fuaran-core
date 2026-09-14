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




