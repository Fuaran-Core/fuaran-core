module Skeleton

let skeleton_fold : TreeOps.tree  ->  Prims.list<Prims.list<TreeOps.leaf_op>>  ->  DagFold.lane_outcome<TreeOps.leaf_op, TreeOps.tree, TreeOps.rejection> = (fun ( s0  :  TreeOps.tree ) ( lanes  :  Prims.list<Prims.list<TreeOps.leaf_op>> ) -> (DagFold.fold_once TreeOps.wapply TreeOps.leaf_fp s0 lanes))




