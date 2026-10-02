module Normalize

let collapse : TreeOps.op  ->  TreeOps.op  ->  FStar_Pervasives_Native.option<Prims.list<TreeOps.op>> = (fun ( top  :  TreeOps.op ) ( x  :  TreeOps.op ) -> (match (((top), (x))) with
| (TreeOps.InsertChild (uu___, n), TreeOps.RemoveNode (y)) -> begin
      
if (Prims.op_Equals (TreeOps.tid_of n) y) then begin
     FStar_Pervasives_Native.Some ([])
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (TreeOps.MoveNode (t1, uu___), TreeOps.MoveNode (t2, uu___1)) -> begin
      
if (Prims.op_Equals t1 t2) then begin
     FStar_Pervasives_Native.Some ((x)::[])
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (TreeOps.ReorderChildren (p1, uu___), TreeOps.ReorderChildren (p2, uu___1)) -> begin
      
if (Prims.op_Equals p1 p2) then begin
     FStar_Pervasives_Native.Some ((x)::[])
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (TreeOps.UpdateNode (a), TreeOps.UpdateNode (a')) -> begin
      
if (Prims.op_Equals (TreeOps.tid_of a) (TreeOps.tid_of a')) then begin
     FStar_Pervasives_Native.Some ((x)::[])
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (TreeOps.InsertChild (p, n), TreeOps.UpdateNode (n')) -> begin
      
if (Prims.op_Equals (TreeOps.tid_of n) (TreeOps.tid_of n')) then begin
     FStar_Pervasives_Native.Some ((TreeOps.InsertChild (p, TreeOps.TNode ((TreeOps.tid_of n'), (TreeOps.kind_of n'), (TreeOps.kids_of n))))::[])
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (TreeOps.UpdateNode (n), TreeOps.RemoveNode (y)) -> begin
      
if (Prims.op_Equals (TreeOps.tid_of n) y) then begin
     FStar_Pervasives_Native.Some ((x)::[])
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end))


let rec push : Prims.list<TreeOps.op>  ->  TreeOps.op  ->  Prims.list<TreeOps.op> = (fun ( stack  :  Prims.list<TreeOps.op> ) ( x  :  TreeOps.op ) -> (match (stack) with
| [] -> begin
     (x)::[]
     end
| (top)::rest -> begin
     (match ((collapse top x)) with
| FStar_Pervasives_Native.None -> begin
     (x)::stack
     end
| FStar_Pervasives_Native.Some ([]) -> begin
     rest
     end
| FStar_Pervasives_Native.Some ((y)::uu___) -> begin
     (push rest y)
     end)
     end))


let rec norm_go : Prims.list<TreeOps.op>  ->  Prims.list<TreeOps.op>  ->  Prims.list<TreeOps.op> = (fun ( stack  :  Prims.list<TreeOps.op> ) ( os  :  Prims.list<TreeOps.op> ) -> (match (os) with
| [] -> begin
     stack
     end
| (o)::r -> begin
     (norm_go (norm_step stack o) r)
     end))
and norm_step : Prims.list<TreeOps.op>  ->  TreeOps.op  ->  Prims.list<TreeOps.op> = (fun ( stack  :  Prims.list<TreeOps.op> ) ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.Batch (inner) -> begin
     (match ((DagFold.rev (norm_go [] inner))) with
| [] -> begin
     stack
     end
| xs -> begin
     (push stack (TreeOps.Batch (xs)))
     end)
     end
| uu___ -> begin
     (push stack o)
     end))


let normalize : Prims.list<TreeOps.op>  ->  Prims.list<TreeOps.op> = (fun ( os  :  Prims.list<TreeOps.op> ) -> (DagFold.rev (norm_go [] os)))


let rec apply_stack : Prims.list<TreeOps.op>  ->  TreeOps.tree  ->  DagFold.outcome<TreeOps.tree, TreeOps.rejection> = (fun ( stack  :  Prims.list<TreeOps.op> ) ( t  :  TreeOps.tree ) -> (match (stack) with
| [] -> begin
     DagFold.Ok (t)
     end
| (top)::rest -> begin
     (match ((apply_stack rest t)) with
| DagFold.Ok (u) -> begin
     (TreeOps.apply top u)
     end
| DagFold.Error (e) -> begin
     DagFold.Error (e)
     end)
     end))


let rec stable : Prims.list<TreeOps.op>  ->  Prims.bool = (fun ( l  :  Prims.list<TreeOps.op> ) -> (match (l) with
| [] -> begin
     true
     end
| (a)::r -> begin
     (((stable_op a) && (match (r) with
| [] -> begin
     true
     end
| (b)::uu___ -> begin
     (match ((collapse a b)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| FStar_Pervasives_Native.Some (uu___1) -> begin
     false
     end)
     end)) && (stable r))
     end))
and stable_op : TreeOps.op  ->  Prims.bool = (fun ( o  :  TreeOps.op ) -> (match (o) with
| TreeOps.Batch (inner) -> begin
     ((match (inner) with
| [] -> begin
     false
     end
| uu___ -> begin
     true
     end) && (stable inner))
     end
| uu___ -> begin
     true
     end))


let rec sstable : Prims.list<TreeOps.op>  ->  Prims.bool = (fun ( stack  :  Prims.list<TreeOps.op> ) -> (match (stack) with
| [] -> begin
     true
     end
| (a)::r -> begin
     (((stable_op a) && (match (r) with
| [] -> begin
     true
     end
| (b)::uu___ -> begin
     (match ((collapse b a)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| FStar_Pervasives_Native.Some (uu___1) -> begin
     false
     end)
     end)) && (sstable r))
     end))


let rec last_op : Prims.list<TreeOps.op>  ->  FStar_Pervasives_Native.option<TreeOps.op> = (fun ( l  :  Prims.list<TreeOps.op> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (a)::[] -> begin
     FStar_Pervasives_Native.Some (a)
     end
| (uu___)::r -> begin
     (last_op r)
     end))


let cx_tree : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("a", "sec", []))::[])


let cx_script : Prims.list<TreeOps.op> = (TreeOps.InsertChild ("a", TreeOps.TNode ("a", "para", [])))::(TreeOps.RemoveNode ("a"))::[]

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


let twins : Prims.list<twin> = ({tname = "normalize-cancels-an-insert-by-its-remove"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (normalize ((TreeOps.InsertChild ("root", TreeOps.TNode ("n", "para", [])))::(TreeOps.RemoveNode ("n"))::[])) []))})::({tname = "normalize-keeps-the-last-reorder-on-a-parent"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (normalize ((TreeOps.ReorderChildren ("p", ("b")::("a")::[]))::(TreeOps.ReorderChildren ("p", ("a")::("b")::[]))::[])) ((TreeOps.ReorderChildren ("p", ("a")::("b")::[]))::[])))})::({tname = "normalize-folds-a-rewrite-into-the-insert-it-follows"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (normalize ((TreeOps.InsertChild ("root", TreeOps.TNode ("n", "para", (TreeOps.TNode ("c", "para", []))::[])))::(TreeOps.UpdateNode (TreeOps.TNode ("n", "aside", [])))::[])) ((TreeOps.InsertChild ("root", TreeOps.TNode ("n", "aside", (TreeOps.TNode ("c", "para", []))::[])))::[])))})::({tname = "normalize-catches-the-adjacency-a-cancellation-exposes"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (normalize ((TreeOps.MoveNode ("x", "a"))::(TreeOps.InsertChild ("a", TreeOps.TNode ("n", "para", [])))::(TreeOps.RemoveNode ("n"))::(TreeOps.MoveNode ("x", "b"))::[])) ((TreeOps.MoveNode ("x", "b"))::[])))})::({tname = "normalize-drops-an-empty-batch-and-normalises-inside-one"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (normalize ((TreeOps.Batch ([]))::(TreeOps.Batch ((TreeOps.UpdateNode (TreeOps.TNode ("a", "k1", [])))::(TreeOps.UpdateNode (TreeOps.TNode ("a", "k2", [])))::[]))::[])) ((TreeOps.Batch ((TreeOps.UpdateNode (TreeOps.TNode ("a", "k2", [])))::[]))::[])))})::[]




