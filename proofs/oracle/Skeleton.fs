module Skeleton

let skeleton_fold : TreeOps.tree  ->  Prims.list<Prims.list<TreeOps.op>>  ->  DagFold.lane_outcome<TreeOps.op, TreeOps.tree, TreeOps.rejection> = (fun ( s0  :  TreeOps.tree ) ( lanes  :  Prims.list<Prims.list<TreeOps.op>> ) -> (DagFold.fold_once TreeOps.wapply TreeOps.op_fp s0 lanes))


let update_tree : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("x", "sec", []))::(TreeOps.TNode ("y", "sec", []))::[])


let twin_halted : DagFold.lane_outcome<TreeOps.op, TreeOps.tree, TreeOps.rejection>  ->  Prims.bool = (fun ( o  :  DagFold.lane_outcome<TreeOps.op, TreeOps.tree, TreeOps.rejection> ) -> (match (o) with
| DagFold.LaneHalted (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let twins : Prims.list<Twin.twin> = ({Twin.tname = "skeleton-fold-of-one-update-lane"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (skeleton_fold update_tree (((TreeOps.UpdateNode (TreeOps.TNode ("x", "aside", [])))::[])::[])) (DagFold.LaneFolded (TreeOps.TNode ("root", "doc", (TreeOps.TNode ("x", "aside", []))::(TreeOps.TNode ("y", "sec", []))::[])))))})::({Twin.tname = "skeleton-fold-of-no-lanes"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (skeleton_fold update_tree []) (DagFold.LaneFolded (update_tree))))})::({Twin.tname = "skeleton-fold-halts-two-updates"; Twin.tholds = (fun ( uu___  :  unit ) -> (twin_halted (skeleton_fold update_tree (((TreeOps.UpdateNode (TreeOps.TNode ("x", "aside", [])))::[])::((TreeOps.UpdateNode (TreeOps.TNode ("y", "aside", [])))::[])::[]))))})::[]




