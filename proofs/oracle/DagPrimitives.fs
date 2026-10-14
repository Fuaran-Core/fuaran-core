module DagPrimitives

let rec parents_of_all = (fun ( d  :  DagFold.dag<'op> ) ( ids  :  Prims.list<Prims.string> ) -> (match (ids) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (match ((DagFold.lookup d.nodes x)) with
| DagFold.Found (n) -> begin
     (DagFold.app n.nparents (parents_of_all d t))
     end
| DagFold.Missing -> begin
     (parents_of_all d t)
     end)
     end))


let rec all_in : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) ( m  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((DagFold.mem x m) && (all_in t m))
     end))


let not_in : Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( below  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (not ((DagFold.mem x below))))


let deepest_cands = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( common  :  Prims.list<Prims.string> ) -> (

let ns = (DagFold.closure_nodes d.nodes common)
in  
if (all_in (DagFold.ids_of ns) (DagFold.drain_order lt kfuel ns)) then begin
     (match (fuel) with
| [] -> begin
     common
     end
| (uu___)::f' -> begin
     (DagFold.keep (not_in (DagFold.ancestors_all d f' (parents_of_all d common))) common)
     end)
     end else begin
     common
     end))


let deepest = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( c0  :  Prims.string ) ( t  :  Prims.list<Prims.string> ) -> (match ((deepest_cands lt kfuel d fuel ((c0)::t))) with
| [] -> begin
     (DagFold.max_by (DagFold.deeper lt d fuel) c0 t)
     end
| (b)::r -> begin
     (DagFold.max_by (DagFold.deeper lt d fuel) b r)
     end))


let merge_base_now = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( left  :  Prims.string ) ( right  :  Prims.string ) -> (match ((DagFold.inter (DagFold.ancestors_of d fuel left) (DagFold.ancestors_of d fuel right))) with
| [] -> begin
     DagFold.Missing
     end
| (c)::t -> begin
     DagFold.Found ((deepest lt kfuel d fuel c t))
     end))


let rec distinct_go : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( seen  :  Prims.list<Prims.string> ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (DagFold.mem x seen) then begin
     (distinct_go seen t)
     end else begin
     (x)::(distinct_go ((x)::seen) t)
     end
     end))


let rec inter_all = (fun ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( acc  :  Prims.list<Prims.string> ) ( heads  :  Prims.list<Prims.string> ) -> (match (heads) with
| [] -> begin
     acc
     end
| (x)::t -> begin
     (inter_all d fuel (DagFold.inter acc (DagFold.ancestors_of d fuel x)) t)
     end))


let common_base = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( heads  :  Prims.list<Prims.string> ) -> (match ((distinct_go [] heads)) with
| [] -> begin
     DagFold.Missing
     end
| (h)::rest -> begin
     (match ((inter_all d fuel (DagFold.ancestors_of d fuel h) rest)) with
| [] -> begin
     DagFold.Missing
     end
| (c)::t -> begin
     DagFold.Found ((deepest lt kfuel d fuel c t))
     end)
     end))


let rec in_all = (fun ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( heads  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (match (heads) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((DagFold.mem x (DagFold.ancestors_of d fuel h)) && (in_all d fuel t x))
     end))


let reach_count : Prims.list<DagFold.rslot>  ->  Prims.string  ->  Prims.list<Prims.string> = (fun ( r  :  Prims.list<DagFold.rslot> ) ( x  :  Prims.string ) -> (match ((DagFold.slot_of r x)) with
| DagFold.Found (b) -> begin
     (DagFold.dedup b)
     end
| DagFold.Missing -> begin
     []
     end))


let reach_deeper : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  Prims.list<DagFold.rslot>  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( r  :  Prims.list<DagFold.rslot> ) ( x  :  Prims.string ) ( y  :  Prims.string ) -> (

let cx = (reach_count r x)
in (

let cy = (reach_count r y)
in  
if (DagFold.shorter cy cx) then begin
     true
     end else begin
      
if (DagFold.shorter cx cy) then begin
     false
     end else begin
     (lt y x)
     end
     end)))


let reach_merge_base = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( r  :  Prims.list<DagFold.rslot> ) ( left  :  Prims.string ) ( right  :  Prims.string ) -> (match ((DagFold.lookup d.nodes left)) with
| DagFold.Missing -> begin
     DagFold.Missing
     end
| DagFold.Found (uu___) -> begin
     (match ((DagFold.lookup d.nodes right)) with
| DagFold.Missing -> begin
     DagFold.Missing
     end
| DagFold.Found (uu___1) -> begin
     (match ((DagFold.slot_of r left)) with
| DagFold.Missing -> begin
     (merge_base_now lt kfuel d fuel left right)
     end
| DagFold.Found (bl) -> begin
     (match ((DagFold.slot_of r right)) with
| DagFold.Missing -> begin
     (merge_base_now lt kfuel d fuel left right)
     end
| DagFold.Found (br) -> begin
     (match ((DagFold.inter bl br)) with
| [] -> begin
     DagFold.Missing
     end
| (c)::t -> begin
     DagFold.Found ((DagFold.max_by (reach_deeper lt r) c t))
     end)
     end)
     end)
     end)
     end))


let rec all_held = (fun ( d  :  DagFold.dag<'op> ) ( hs  :  Prims.list<Prims.string> ) -> (match (hs) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((match ((DagFold.lookup d.nodes h)) with
| DagFold.Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_held d t))
     end))


let rec all_slotted : Prims.list<DagFold.rslot>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( r  :  Prims.list<DagFold.rslot> ) ( hs  :  Prims.list<Prims.string> ) -> (match (hs) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((match ((DagFold.slot_of r h)) with
| DagFold.Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_slotted r t))
     end))


let rec in_all_bits : Prims.list<DagFold.rslot>  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( r  :  Prims.list<DagFold.rslot> ) ( hs  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> (match (hs) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((match ((DagFold.slot_of r h)) with
| DagFold.Found (b) -> begin
     (DagFold.mem x b)
     end
| DagFold.Missing -> begin
     false
     end) && (in_all_bits r t x))
     end))


let reach_common_base = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( r  :  Prims.list<DagFold.rslot> ) ( heads  :  Prims.list<Prims.string> ) -> (match ((distinct_go [] heads)) with
| [] -> begin
     DagFold.Missing
     end
| (h)::rest -> begin
      
if (not ((all_held d ((h)::rest)))) then begin
     DagFold.Missing
     end else begin
      
if (not ((all_slotted r ((h)::rest)))) then begin
     (common_base lt kfuel d fuel heads)
     end else begin
     (match ((DagFold.slot_of r h)) with
| DagFold.Missing -> begin
     DagFold.Missing
     end
| DagFold.Found (bh) -> begin
     (match ((DagFold.keep (in_all_bits r ((h)::rest)) bh)) with
| [] -> begin
     DagFold.Missing
     end
| (c)::t -> begin
     DagFold.Found ((DagFold.max_by (reach_deeper lt r) c t))
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


let try_topo_order = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( head  :  Prims.string ) -> (

let ord = (DagFold.topo_drain lt kfuel d fuel head)
in  
if (all_in (DagFold.ids_of (DagFold.closure_of d fuel head)) ord) then begin
     TopoOk (ord)
     end else begin
     TopoCyclic (head)
     end))


let reach_try_topo_order = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( r  :  Prims.list<DagFold.rslot> ) ( ord  :  Prims.list<Prims.string> ) ( head  :  Prims.string ) -> (match ((DagFold.slot_of r head)) with
| DagFold.Found (b) -> begin
     TopoOk ((DagFold.keep (DagFold.in_set b) ord))
     end
| DagFold.Missing -> begin
     (try_topo_order lt kfuel d fuel head)
     end))


let head_not_base : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( bh  :  Prims.list<Prims.string> ) ( bb  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> ((DagFold.mem x bh) && (not ((DagFold.mem x bb)))))


let reach_between = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( kfuel  :  Prims.list<DagFold.node<'op>> ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( r  :  Prims.list<DagFold.rslot> ) ( ord  :  Prims.list<Prims.string> ) ( base_id  :  Prims.string ) ( head  :  Prims.string ) -> (match ((DagFold.slot_of r head)) with
| DagFold.Missing -> begin
     (DagFold.between_drained lt kfuel d fuel base_id head)
     end
| DagFold.Found (bh) -> begin
     (match ((DagFold.slot_of r base_id)) with
| DagFold.Found (bb) -> begin
     (DagFold.nodes_for d (DagFold.keep (head_not_base bh bb) ord))
     end
| DagFold.Missing -> begin
     (match ((DagFold.lookup d.nodes base_id)) with
| DagFold.Missing -> begin
     (DagFold.nodes_for d (DagFold.keep (DagFold.in_set bh) ord))
     end
| DagFold.Found (uu___) -> begin
     (DagFold.between_drained lt kfuel d fuel base_id head)
     end)
     end)
     end))


let rec children_of = (fun ( ns  :  Prims.list<DagFold.node<'op>> ) ( p  :  Prims.string ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
      
if (DagFold.mem p n.nparents) then begin
     (n.nid)::(children_of t p)
     end else begin
     (children_of t p)
     end
     end))


let rec fwd_go = (fun ( d  :  DagFold.dag<'op> ) ( within  :  Prims.string  ->  Prims.bool ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( acc  :  Prims.list<Prims.string> ) ( stack  :  Prims.list<Prims.string> ) -> (match (stack) with
| [] -> begin
     DagFold.Found (acc)
     end
| (id)::rest -> begin
      
if ((DagFold.mem id acc) || (not ((within id)))) then begin
     (fwd_go d within fuel acc rest)
     end else begin
     (match (fuel) with
| [] -> begin
     DagFold.Missing
     end
| (uu___)::f' -> begin
     (fwd_go d within f' ((id)::acc) (DagFold.app (children_of d.nodes id) rest))
     end)
     end
     end))


let forward_from = (fun ( d  :  DagFold.dag<'op> ) ( within  :  Prims.string  ->  Prims.bool ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( seeds  :  Prims.list<Prims.string> ) -> (fwd_go d within fuel [] seeds))


let any_id : Prims.string  ->  Prims.bool = (fun ( uu___  :  Prims.string ) -> true)


let not_eq : Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( c  :  Prims.string ) ( x  :  Prims.string ) -> (not ((Prims.op_Equals x c))))


let uncovered : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( behind  :  Prims.list<Prims.string> ) ( later  :  Prims.list<Prims.string> ) ( x  :  Prims.string ) -> ((not ((DagFold.mem x behind))) && (not ((DagFold.mem x later)))))


let side_parent : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.string  ->  Prims.bool = (fun ( c  :  Prims.string ) ( behind  :  Prims.list<Prims.string> ) ( p  :  Prims.string ) -> ((not ((Prims.op_Equals p c))) && (DagFold.mem p behind)))

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


let cut_keep = (fun ( lt  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( d  :  DagFold.dag<'op> ) ( fuel  :  Prims.list<DagFold.node<'op>> ) ( c  :  Prims.string ) -> (

let behind = (DagFold.ancestors_of d fuel c)
in (match ((forward_from d any_id d.nodes ((c)::[]))) with
| DagFold.Missing -> begin
     CutUnderfuelled
     end
| DagFold.Found (fw) -> begin
     (

let later = (DagFold.keep (not_eq c) fw)
in (match ((DagFold.keep (uncovered behind later) (DagFold.ids_of d.nodes))) with
| (u)::us -> begin
     CutUncovered ((DagFold.pick_min lt ((u)::us)))
     end
| [] -> begin
     (

let side = (DagFold.keep (side_parent c behind) (parents_of_all d later))
in (match ((forward_from d (DagFold.in_set behind) d.nodes side)) with
| DagFold.Missing -> begin
     CutUnderfuelled
     end
| DagFold.Found (band) -> begin
     CutKeep ((DagFold.app band (DagFold.app later ((c)::[]))))
     end))
     end))
     end)))


let twin_band : DagFold.dag<Prims.string> = {DagFold.nodes = ({DagFold.nid = "m"; DagFold.nparents = ("c")::("a")::[]; DagFold.nop = "m"})::({DagFold.nid = "c"; DagFold.nparents = ("b")::[]; DagFold.nop = "c"})::({DagFold.nid = "b"; DagFold.nparents = ("a")::[]; DagFold.nop = "b"})::({DagFold.nid = "a"; DagFold.nparents = []; DagFold.nop = "a"})::[]}


let twin_band_fuel : Prims.list<DagFold.node<Prims.string>> = (DagFold.app twin_band.nodes twin_band.nodes)


let twin_reach : Prims.list<DagFold.rslot> = (DagFold.reach_of_dag DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag)


let twin_order : Prims.list<Prims.string> = (DagFold.drain_order DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag.nodes)


let twin_kept : cut_result  ->  DagFold.dag<Prims.string>  ->  Prims.list<DagFold.node<Prims.string>>  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( r  :  cut_result ) ( d  :  DagFold.dag<Prims.string> ) ( fuel  :  Prims.list<DagFold.node<Prims.string>> ) ( c  :  Prims.string ) ( want  :  Prims.list<Prims.string> ) -> (match (r) with
| CutKeep (k) -> begin
     (((DagFold.truncation_ok d fuel c k) && (all_in want k)) && (all_in k want))
     end
| uu___ -> begin
     false
     end))


let twins : Prims.list<DagFold.twin> = ({DagFold.tname = "merge-base-now-of-a-criss-cross-is-the-modelled-one"; DagFold.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (merge_base_now DagFold.twin_lt DagFold.twin_cc_fuel DagFold.twin_cc DagFold.twin_cc_fuel "x" "y") (DagFold.Found ("c"))) && (Prims.op_Equals (merge_base_now DagFold.twin_lt DagFold.twin_cc_fuel DagFold.twin_cc DagFold.twin_cc_fuel "x" "zz") DagFold.Missing)))})::({DagFold.tname = "common-base-over-three-heads"; DagFold.tholds = (fun ( uu___  :  unit ) -> ((((Prims.op_Equals (common_base DagFold.twin_lt DagFold.twin_cc_fuel DagFold.twin_cc DagFold.twin_cc_fuel (("x")::("y")::("x")::[])) (DagFold.Found ("c"))) && (Prims.op_Equals (common_base DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel (("b")::("c")::("m")::[])) (DagFold.Found ("a")))) && (Prims.op_Equals (common_base DagFold.twin_lt DagFold.twin_cc_fuel DagFold.twin_cc DagFold.twin_cc_fuel []) DagFold.Missing)) && (Prims.op_Equals (common_base DagFold.twin_lt DagFold.twin_cc_fuel DagFold.twin_cc DagFold.twin_cc_fuel (("x")::("zz")::[])) DagFold.Missing)))})::({DagFold.tname = "reach-merge-base-and-common-base-answer-the-dag"; DagFold.tholds = (fun ( uu___  :  unit ) -> ((((Prims.op_Equals (reach_merge_base DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel twin_reach "b" "c") (DagFold.Found ("a"))) && (Prims.op_Equals (reach_merge_base DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel twin_reach "m" "c") (DagFold.Found ("c")))) && (Prims.op_Equals (reach_merge_base DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel twin_reach "m" "zz") DagFold.Missing)) && (Prims.op_Equals (reach_common_base DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel twin_reach (("b")::("c")::("m")::[])) (DagFold.Found ("a")))))})::({DagFold.tname = "reach-try-topo-order-is-the-closure-drain"; DagFold.tholds = (fun ( uu___  :  unit ) -> (((Prims.op_Equals (reach_try_topo_order DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel twin_reach twin_order "m") (TopoOk (("a")::("b")::("c")::("m")::[]))) && (Prims.op_Equals (try_topo_order DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel "c") (TopoOk (("a")::("c")::[])))) && (Prims.op_Equals (reach_try_topo_order DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel twin_reach twin_order "c") (TopoOk (("a")::("c")::[])))))})::({DagFold.tname = "reach-between-is-the-delta"; DagFold.tholds = (fun ( uu___  :  unit ) -> (((Prims.op_Equals (DagFold.ids_of (reach_between DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel twin_reach twin_order "b" "m")) (("c")::("m")::[])) && (Prims.op_Equals (DagFold.ids_of (DagFold.between_drained DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel "b" "m")) (("c")::("m")::[]))) && (Prims.op_Equals (DagFold.ids_of (reach_between DagFold.twin_lt DagFold.twin_fuel DagFold.twin_dag DagFold.twin_fuel twin_reach twin_order "zz" "c")) (("a")::("c")::[]))))})::({DagFold.tname = "cut-keeps-what-follows-and-the-band"; DagFold.tholds = (fun ( uu___  :  unit ) -> ((twin_kept (cut_keep DagFold.twin_lt DagFold.twin_chain DagFold.twin_chain_fuel "q") DagFold.twin_chain DagFold.twin_chain_fuel "q" (("q")::("r")::[])) && (twin_kept (cut_keep DagFold.twin_lt twin_band twin_band_fuel "c") twin_band twin_band_fuel "c" (("a")::("b")::("c")::("m")::[]))))})::({DagFold.tname = "cut-refuses-an-uncovered-node"; DagFold.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (cut_keep DagFold.twin_lt DagFold.twin_dag DagFold.twin_fuel "b") (CutUncovered ("c"))))})::[]




