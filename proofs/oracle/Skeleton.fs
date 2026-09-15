module Skeleton

let skeleton_fold : TreeOps.tree  ->  Prims.list<Prims.list<TreeOps.op>>  ->  DagFold.lane_outcome<TreeOps.op, TreeOps.tree, TreeOps.rejection> = (fun ( s0  :  TreeOps.tree ) ( lanes  :  Prims.list<Prims.list<TreeOps.op>> ) -> (DagFold.fold_once TreeOps.wapply TreeOps.op_fp s0 lanes))




