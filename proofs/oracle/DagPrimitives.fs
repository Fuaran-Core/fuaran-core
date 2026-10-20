module DagPrimitives

let rec parents_of_all = (fun ( d  :  DagClosure.dag<'op> ) ( ids  :  Prims.list<Prims.string> ) -> (match (ids) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (match ((DagClosure.lookup d.nodes x)) with
| DagClosure.Found (n) -> begin
     (ListAlgebra.app n.nparents (parents_of_all d t))
     end
| DagClosure.Missing -> begin
     (parents_of_all d t)
     end)
     end))


let rec all_in : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) ( m  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((ListAlgebra.mem x m) && (all_in t m))
     end))


let not_in : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( below  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (not ((ListAlgebra.mem x below))))


let deepest_cands = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( common  :  Prims.list<Prims.string> ) -> (

let ns = (DagClosure.closure_nodes d.nodes common)
in  
if (all_in (DagClosure.ids_of ns) (DagClosure.drain_order lt kfuel ns)) then begin
     (match (fuel) with
| [] -> begin
     common
     end
| (uu___)::f' -> begin
     (DagFold.keep (not_in (DagClosure.ancestors_all d f' (parents_of_all d common))) common)
     end)
     end else begin
     common
     end))


let deepest = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( c0  :  Prims.string ) ( t  :  Prims.list<Prims.string> ) -> (match ((deepest_cands lt kfuel d fuel ((c0)::t))) with
| [] -> begin
     (DagClosure.max_by (DagClosure.deeper lt d fuel) c0 t)
     end
| (b)::r -> begin
     (DagClosure.max_by (DagClosure.deeper lt d fuel) b r)
     end))


let merge_base_now = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( left  :  Prims.string ) ( right  :  Prims.string ) -> (match ((ListAlgebra.inter (DagClosure.ancestors_of d fuel left) (DagClosure.ancestors_of d fuel right))) with
| [] -> begin
     DagClosure.Missing
     end
| (c)::t -> begin
     DagClosure.Found ((deepest lt kfuel d fuel c t))
     end))


let rec distinct_go : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( seen  :  Prims.list<Prims.string> ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (ListAlgebra.mem x seen) then begin
     (distinct_go seen t)
     end else begin
     (x)::(distinct_go ((x)::seen) t)
     end
     end))


let rec inter_all = (fun ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( acc  :  Prims.list<Prims.string> ) ( heads  :  Prims.list<Prims.string> ) -> (match (heads) with
| [] -> begin
     acc
     end
| (x)::t -> begin
     (inter_all d fuel (ListAlgebra.inter acc (DagClosure.ancestors_of d fuel x)) t)
     end))


let common_base = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( heads  :  Prims.list<Prims.string> ) -> (match ((distinct_go [] heads)) with
| [] -> begin
     DagClosure.Missing
     end
| (h)::rest -> begin
     (match ((inter_all d fuel (DagClosure.ancestors_of d fuel h) rest)) with
| [] -> begin
     DagClosure.Missing
     end
| (c)::t -> begin
     DagClosure.Found ((deepest lt kfuel d fuel c t))
     end)
     end))


let rec in_all = (fun ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( heads  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (match (heads) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((ListAlgebra.mem x (DagClosure.ancestors_of d fuel h)) && (in_all d fuel t x))
     end))


let reach_count : Prims.list<DagClosure.rslot>  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( r  :  Prims.list<DagClosure.rslot> ) ( x  :  Prims.string ) -> (match ((DagClosure.slot_of r x)) with
| DagClosure.Found (b) -> begin
     (DagClosure.dedup b)
     end
| DagClosure.Missing -> begin
     []
     end))


let reach_deeper : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  Prims.list<DagClosure.rslot>  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( r  :  Prims.list<DagClosure.rslot> ) ( x  :  Prims.string ) ( y  :  Prims.string ) -> (

let cx = (reach_count r x)
in (

let cy = (reach_count r y)
in  
if (DagClosure.shorter cy cx) then begin
     true
     end else begin
      
if (DagClosure.shorter cx cy) then begin
     false
     end else begin
     (lt y x)
     end
     end)))


let reach_merge_base = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( r  :  Prims.list<DagClosure.rslot> ) ( left  :  Prims.string ) ( right  :  Prims.string ) -> (match ((DagClosure.lookup d.nodes left)) with
| DagClosure.Missing -> begin
     DagClosure.Missing
     end
| DagClosure.Found (uu___) -> begin
     (match ((DagClosure.lookup d.nodes right)) with
| DagClosure.Missing -> begin
     DagClosure.Missing
     end
| DagClosure.Found (uu___1) -> begin
     (match ((DagClosure.slot_of r left)) with
| DagClosure.Missing -> begin
     (merge_base_now lt kfuel d fuel left right)
     end
| DagClosure.Found (bl) -> begin
     (match ((DagClosure.slot_of r right)) with
| DagClosure.Missing -> begin
     (merge_base_now lt kfuel d fuel left right)
     end
| DagClosure.Found (br) -> begin
     (match ((ListAlgebra.inter bl br)) with
| [] -> begin
     DagClosure.Missing
     end
| (c)::t -> begin
     DagClosure.Found ((DagClosure.max_by (reach_deeper lt r) c t))
     end)
     end)
     end)
     end)
     end))


let rec all_held = (fun ( d  :  DagClosure.dag<'op> ) ( hs  :  Prims.list<Prims.string> ) -> (match (hs) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((match ((DagClosure.lookup d.nodes h)) with
| DagClosure.Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_held d t))
     end))


let rec all_slotted : Prims.list<DagClosure.rslot>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( r  :  Prims.list<DagClosure.rslot> ) ( hs  :  Prims.list<Prims.string> ) -> (match (hs) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((match ((DagClosure.slot_of r h)) with
| DagClosure.Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_slotted r t))
     end))


let rec in_all_bits : Prims.list<DagClosure.rslot>  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( r  :  Prims.list<DagClosure.rslot> ) ( hs  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (match (hs) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((match ((DagClosure.slot_of r h)) with
| DagClosure.Found (b) -> begin
     (ListAlgebra.mem x b)
     end
| DagClosure.Missing -> begin
     false
     end) && (in_all_bits r t x))
     end))


let reach_common_base = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( r  :  Prims.list<DagClosure.rslot> ) ( heads  :  Prims.list<Prims.string> ) -> (match ((distinct_go [] heads)) with
| [] -> begin
     DagClosure.Missing
     end
| (h)::rest -> begin
      
if (not ((all_held d ((h)::rest)))) then begin
     DagClosure.Missing
     end else begin
      
if (not ((all_slotted r ((h)::rest)))) then begin
     (common_base lt kfuel d fuel heads)
     end else begin
     (match ((DagClosure.slot_of r h)) with
| DagClosure.Missing -> begin
     DagClosure.Missing
     end
| DagClosure.Found (bh) -> begin
     (match ((DagFold.keep (in_all_bits r ((h)::rest)) bh)) with
| [] -> begin
     DagClosure.Missing
     end
| (c)::t -> begin
     DagClosure.Found ((DagClosure.max_by (reach_deeper lt r) c t))
     end)
     end)
     end
     end
     end))

type topo_result =
| TopoOk of Prims.list<Prims.string>
| TopoCyclic of Prims.string


let uu___is_TopoOk : topo_result  ->  Prims.bool = (fun ( projectee  :  topo_result ) -> (match (projectee) with
| TopoOk (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TopoOk__item___0 : topo_result  ->  Prims.list<Prims.string> = (fun ( projectee  :  topo_result ) -> (match (projectee) with
| TopoOk (_0) -> begin
     _0
     end))


let uu___is_TopoCyclic : topo_result  ->  Prims.bool = (fun ( projectee  :  topo_result ) -> (match (projectee) with
| TopoCyclic (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TopoCyclic__item___0 : topo_result  ->  Prims.string = (fun ( projectee  :  topo_result ) -> (match (projectee) with
| TopoCyclic (_0) -> begin
     _0
     end))


let try_topo_order = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( head  :  Prims.string ) -> (

let ord = (DagClosure.topo_drain lt kfuel d fuel head)
in  
if (all_in (DagClosure.ids_of (DagClosure.closure_of d fuel head)) ord) then begin
     TopoOk (ord)
     end else begin
     TopoCyclic (head)
     end))


let reach_try_topo_order = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( r  :  Prims.list<DagClosure.rslot> ) ( ord  :  Prims.list<Prims.string> ) ( head  :  Prims.string ) -> (match ((DagClosure.slot_of r head)) with
| DagClosure.Found (b) -> begin
     TopoOk ((DagFold.keep (DagClosure.in_set b) ord))
     end
| DagClosure.Missing -> begin
     (try_topo_order lt kfuel d fuel head)
     end))


let head_not_base : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( bh  :  Prims.list<Prims.string> ) ( bb  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> ((ListAlgebra.mem x bh) && (not ((ListAlgebra.mem x bb)))))


let reach_between = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagClosure.node<'op>> ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( r  :  Prims.list<DagClosure.rslot> ) ( ord  :  Prims.list<Prims.string> ) ( base_id  :  Prims.string ) ( head  :  Prims.string ) -> (match ((DagClosure.slot_of r head)) with
| DagClosure.Missing -> begin
     (DagClosure.between_drained lt kfuel d fuel base_id head)
     end
| DagClosure.Found (bh) -> begin
     (match ((DagClosure.slot_of r base_id)) with
| DagClosure.Found (bb) -> begin
     (DagClosure.nodes_for d (DagFold.keep (head_not_base bh bb) ord))
     end
| DagClosure.Missing -> begin
     (match ((DagClosure.lookup d.nodes base_id)) with
| DagClosure.Missing -> begin
     (DagClosure.nodes_for d (DagFold.keep (DagClosure.in_set bh) ord))
     end
| DagClosure.Found (uu___) -> begin
     (DagClosure.between_drained lt kfuel d fuel base_id head)
     end)
     end)
     end))


let rec children_of = (fun ( ns  :  Prims.list<DagClosure.node<'op>> ) ( p  :  Prims.string ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
      
if (ListAlgebra.mem p n.nparents) then begin
     (n.nid)::(children_of t p)
     end else begin
     (children_of t p)
     end
     end))


let rec fwd_go = (fun ( d  :  DagClosure.dag<'op> ) ( within  :  Prims.string  ->  Prims.bool ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( acc  :  Prims.list<Prims.string> ) ( stack  :  Prims.list<Prims.string> ) -> (match (stack) with
| [] -> begin
     DagClosure.Found (acc)
     end
| (id)::rest -> begin
      
if ((ListAlgebra.mem id acc) || (not ((within id)))) then begin
     (fwd_go d within fuel acc rest)
     end else begin
     (match (fuel) with
| [] -> begin
     DagClosure.Missing
     end
| (uu___)::f' -> begin
     (fwd_go d within f' ((id)::acc) (ListAlgebra.app (children_of d.nodes id) rest))
     end)
     end
     end))


let forward_from = (fun ( d  :  DagClosure.dag<'op> ) ( within  :  Prims.string  ->  Prims.bool ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( seeds  :  Prims.list<Prims.string> ) -> (fwd_go d within fuel [] seeds))


let any_id : Prims.string  ->  Prims.bool = (fun ( uu___  :  Prims.string ) -> true)


let not_eq : Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( c  :  Prims.string ) ( x  :  Prims.string ) -> (not ((Prims.op_Equals x c))))


let uncovered : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( behind  :  Prims.list<Prims.string> ) ( later  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> ((not ((ListAlgebra.mem x behind))) && (not ((ListAlgebra.mem x later)))))


let side_parent : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( c  :  Prims.string ) ( behind  :  Prims.list<Prims.string> ) ( p  :  Prims.string ) -> ((not ((Prims.op_Equals p c))) && (ListAlgebra.mem p behind)))

type cut_result =
| CutKeep of Prims.list<Prims.string>
| CutUncovered of Prims.string
| CutUnderfuelled


let uu___is_CutKeep : cut_result  ->  Prims.bool = (fun ( projectee  :  cut_result ) -> (match (projectee) with
| CutKeep (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CutKeep__item___0 : cut_result  ->  Prims.list<Prims.string> = (fun ( projectee  :  cut_result ) -> (match (projectee) with
| CutKeep (_0) -> begin
     _0
     end))


let uu___is_CutUncovered : cut_result  ->  Prims.bool = (fun ( projectee  :  cut_result ) -> (match (projectee) with
| CutUncovered (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CutUncovered__item___0 : cut_result  ->  Prims.string = (fun ( projectee  :  cut_result ) -> (match (projectee) with
| CutUncovered (_0) -> begin
     _0
     end))


let uu___is_CutUnderfuelled : cut_result  ->  Prims.bool = (fun ( projectee  :  cut_result ) -> (match (projectee) with
| CutUnderfuelled -> begin
     true
     end
| uu___ -> begin
     false
     end))


let cut_keep = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( d  :  DagClosure.dag<'op> ) ( fuel  :  Prims.list<DagClosure.node<'op>> ) ( c  :  Prims.string ) -> (

let behind = (DagClosure.ancestors_of d fuel c)
in (match ((forward_from d any_id d.nodes ((c)::[]))) with
| DagClosure.Missing -> begin
     CutUnderfuelled
     end
| DagClosure.Found (fw) -> begin
     (

let later = (DagFold.keep (not_eq c) fw)
in (match ((DagFold.keep (uncovered behind later) (DagClosure.ids_of d.nodes))) with
| (u)::us -> begin
     CutUncovered ((DagClosure.pick_min lt ((u)::us)))
     end
| [] -> begin
     (

let side = (DagFold.keep (side_parent c behind) (parents_of_all d later))
in (match ((forward_from d (DagClosure.in_set behind) d.nodes side)) with
| DagClosure.Missing -> begin
     CutUnderfuelled
     end
| DagClosure.Found (band) -> begin
     CutKeep ((ListAlgebra.app band (ListAlgebra.app later ((c)::[]))))
     end))
     end))
     end)))


let twin_band : DagClosure.dag<Prims.string> = {DagClosure.nodes = ({DagClosure.nid = "m"; DagClosure.nparents = ("c")::("a")::[]; DagClosure.nop = "m"})::({DagClosure.nid = "c"; DagClosure.nparents = ("b")::[]; DagClosure.nop = "c"})::({DagClosure.nid = "b"; DagClosure.nparents = ("a")::[]; DagClosure.nop = "b"})::({DagClosure.nid = "a"; DagClosure.nparents = []; DagClosure.nop = "a"})::[]}


let twin_band_fuel : Prims.list<DagClosure.node<Prims.string>> = (ListAlgebra.app twin_band.nodes twin_band.nodes)


let twin_reach : Prims.list<DagClosure.rslot> = (DagClosure.reach_of_dag DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag)


let twin_order : Prims.list<Prims.string> = (DagClosure.drain_order DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag.nodes)


let twin_kept : cut_result  ->  DagClosure.dag<Prims.string>  ->  Prims.list<DagClosure.node<Prims.string>>  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( r  :  cut_result ) ( d  :  DagClosure.dag<Prims.string> ) ( fuel  :  Prims.list<DagClosure.node<Prims.string>> ) ( c  :  Prims.string ) ( want  :  Prims.list<Prims.string> ) -> (match (r) with
| CutKeep (k) -> begin
     (((DagClosure.truncation_ok d fuel c k) && (all_in want k)) && (all_in k want))
     end
| uu___ -> begin
     false
     end))


let twins : Prims.list<Twin.twin> = ({Twin.tname = "merge-base-now-of-a-criss-cross-is-the-modelled-one"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (merge_base_now DagClosure.twin_lt DagClosure.twin_cc_fuel DagClosure.twin_cc DagClosure.twin_cc_fuel "x" "y") (DagClosure.Found ("c"))) && (Prims.op_Equals (merge_base_now DagClosure.twin_lt DagClosure.twin_cc_fuel DagClosure.twin_cc DagClosure.twin_cc_fuel "x" "zz") DagClosure.Missing)))})::({Twin.tname = "common-base-over-three-heads"; Twin.tholds = (fun ( uu___  :  unit ) -> ((((Prims.op_Equals (common_base DagClosure.twin_lt DagClosure.twin_cc_fuel DagClosure.twin_cc DagClosure.twin_cc_fuel (("x")::("y")::("x")::[])) (DagClosure.Found ("c"))) && (Prims.op_Equals (common_base DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel (("b")::("c")::("m")::[])) (DagClosure.Found ("a")))) && (Prims.op_Equals (common_base DagClosure.twin_lt DagClosure.twin_cc_fuel DagClosure.twin_cc DagClosure.twin_cc_fuel []) DagClosure.Missing)) && (Prims.op_Equals (common_base DagClosure.twin_lt DagClosure.twin_cc_fuel DagClosure.twin_cc DagClosure.twin_cc_fuel (("x")::("zz")::[])) DagClosure.Missing)))})::({Twin.tname = "reach-merge-base-and-common-base-answer-the-dag"; Twin.tholds = (fun ( uu___  :  unit ) -> ((((Prims.op_Equals (reach_merge_base DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel twin_reach "b" "c") (DagClosure.Found ("a"))) && (Prims.op_Equals (reach_merge_base DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel twin_reach "m" "c") (DagClosure.Found ("c")))) && (Prims.op_Equals (reach_merge_base DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel twin_reach "m" "zz") DagClosure.Missing)) && (Prims.op_Equals (reach_common_base DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel twin_reach (("b")::("c")::("m")::[])) (DagClosure.Found ("a")))))})::({Twin.tname = "reach-try-topo-order-is-the-closure-drain"; Twin.tholds = (fun ( uu___  :  unit ) -> (((Prims.op_Equals (reach_try_topo_order DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel twin_reach twin_order "m") (TopoOk (("a")::("b")::("c")::("m")::[]))) && (Prims.op_Equals (try_topo_order DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel "c") (TopoOk (("a")::("c")::[])))) && (Prims.op_Equals (reach_try_topo_order DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel twin_reach twin_order "c") (TopoOk (("a")::("c")::[])))))})::({Twin.tname = "reach-between-is-the-delta"; Twin.tholds = (fun ( uu___  :  unit ) -> (((Prims.op_Equals (DagClosure.ids_of (reach_between DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel twin_reach twin_order "b" "m")) (("c")::("m")::[])) && (Prims.op_Equals (DagClosure.ids_of (DagClosure.between_drained DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel "b" "m")) (("c")::("m")::[]))) && (Prims.op_Equals (DagClosure.ids_of (reach_between DagClosure.twin_lt DagClosure.twin_fuel DagClosure.twin_dag DagClosure.twin_fuel twin_reach twin_order "zz" "c")) (("a")::("c")::[]))))})::({Twin.tname = "cut-keeps-what-follows-and-the-band"; Twin.tholds = (fun ( uu___  :  unit ) -> ((twin_kept (cut_keep DagClosure.twin_lt DagClosure.twin_chain DagClosure.twin_chain_fuel "q") DagClosure.twin_chain DagClosure.twin_chain_fuel "q" (("q")::("r")::[])) && (twin_kept (cut_keep DagClosure.twin_lt twin_band twin_band_fuel "c") twin_band twin_band_fuel "c" (("a")::("b")::("c")::("m")::[]))))})::({Twin.tname = "cut-refuses-an-uncovered-node"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (cut_keep DagClosure.twin_lt DagClosure.twin_dag DagClosure.twin_fuel "b") (CutUncovered ("c"))))})::[]




