module Arbitrate
type proposal = {pid : Prims.int; holder : Prims.string; script : Prims.list<TreeOps.op>}


let __proj__Mkproposal__item__pid : proposal  ->  Prims.int = (fun ( projectee  :  proposal ) -> (match (projectee) with
| {pid = pid; holder = holder; script = script} -> begin
     pid
     end))


let __proj__Mkproposal__item__holder : proposal  ->  Prims.string = (fun ( projectee  :  proposal ) -> (match (projectee) with
| {pid = pid; holder = holder; script = script} -> begin
     holder
     end))


let __proj__Mkproposal__item__script : proposal  ->  Prims.list<TreeOps.op> = (fun ( projectee  :  proposal ) -> (match (projectee) with
| {pid = pid; holder = holder; script = script} -> begin
     script
     end))

type arb_rejection =
| Inapplicable of Prims.nat * TreeOps.rejection
| Conflicts of Prims.list<Prims.int>


let uu___is_Inapplicable : arb_rejection  ->  Prims.bool = (fun ( projectee  :  arb_rejection ) -> (match (projectee) with
| Inapplicable (op_index, rej) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Inapplicable__item__op_index : arb_rejection  ->  Prims.nat = (fun ( projectee  :  arb_rejection ) -> (match (projectee) with
| Inapplicable (op_index, rej) -> begin
     op_index
     end))


let __proj__Inapplicable__item__rej : arb_rejection  ->  TreeOps.rejection = (fun ( projectee  :  arb_rejection ) -> (match (projectee) with
| Inapplicable (op_index, rej) -> begin
     rej
     end))


let uu___is_Conflicts : arb_rejection  ->  Prims.bool = (fun ( projectee  :  arb_rejection ) -> (match (projectee) with
| Conflicts (interfering) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Conflicts__item__interfering : arb_rejection  ->  Prims.list<Prims.int> = (fun ( projectee  :  arb_rejection ) -> (match (projectee) with
| Conflicts (interfering) -> begin
     interfering
     end))

type arbitration = {accepted : Prims.list<proposal>; merged : Prims.list<TreeOps.op>; rejected : Prims.list<(proposal * arb_rejection)>}


let __proj__Mkarbitration__item__accepted : arbitration  ->  Prims.list<proposal> = (fun ( projectee  :  arbitration ) -> (match (projectee) with
| {accepted = accepted; merged = merged; rejected = rejected} -> begin
     accepted
     end))


let __proj__Mkarbitration__item__merged : arbitration  ->  Prims.list<TreeOps.op> = (fun ( projectee  :  arbitration ) -> (match (projectee) with
| {accepted = accepted; merged = merged; rejected = rejected} -> begin
     merged
     end))


let __proj__Mkarbitration__item__rejected : arbitration  ->  Prims.list<(proposal * arb_rejection)> = (fun ( projectee  :  arbitration ) -> (match (projectee) with
| {accepted = accepted; merged = merged; rejected = rejected} -> begin
     rejected
     end))


let fp_of : proposal  ->  DagFold.footprint = (fun ( p  :  proposal ) -> (TreeOps.fp_all p.script))


let rec can_script : Prims.nat  ->  Prims.list<TreeOps.op>  ->  TreeOps.tree  ->  DagFold.outcome<unit, (Prims.nat * TreeOps.rejection)> = (fun ( i  :  Prims.nat ) ( os  :  Prims.list<TreeOps.op> ) ( t  :  TreeOps.tree ) -> (match (os) with
| [] -> begin
     DagFold.Ok (())
     end
| (o)::r -> begin
     (match ((TreeOps.apply o t)) with
| DagFold.Ok (t') -> begin
     (can_script (i + (Prims.parse_int "1")) r t')
     end
| DagFold.Error (e) -> begin
     DagFold.Error (((i), (e)))
     end)
     end))


let rec insert_by_id : proposal  ->  Prims.list<proposal>  ->  Prims.list<proposal> = (fun ( p  :  proposal ) ( l  :  Prims.list<proposal> ) -> (match (l) with
| [] -> begin
     (p)::[]
     end
| (q)::r -> begin
      
if (p.pid <= q.pid) then begin
     (p)::l
     end else begin
     (q)::(insert_by_id p r)
     end
     end))


let rec pin : Prims.list<proposal>  ->  Prims.list<proposal> = (fun ( ps  :  Prims.list<proposal> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::r -> begin
     (insert_by_id p (pin r))
     end))


let rec all_independent : DagFold.footprint  ->  Prims.list<proposal>  ->  Prims.bool = (fun ( fp  :  DagFold.footprint ) ( acc  :  Prims.list<proposal> ) -> (match (acc) with
| [] -> begin
     true
     end
| (a)::r -> begin
     ((DagFold.independent fp (fp_of a)) && (all_independent fp r))
     end))


type verdicts = (Prims.list<proposal> * Prims.list<(proposal * arb_rejection)>)


let step : TreeOps.tree  ->  verdicts  ->  proposal  ->  verdicts = (fun ( base1  :  TreeOps.tree ) ( st  :  verdicts ) ( p  :  proposal ) -> (match (st) with
| (acc, rej) -> begin
     (match ((can_script (Prims.parse_int "0") p.script base1)) with
| DagFold.Error (i, e) -> begin
     ((acc), ((((p), (Inapplicable (i, e))))::rej))
     end
| DagFold.Ok (()) -> begin
      
if (all_independent (fp_of p) acc) then begin
     (((p)::acc), (rej))
     end else begin
     ((acc), ((((p), (Conflicts ([]))))::rej))
     end
     end)
     end))


let rec fold_step : TreeOps.tree  ->  verdicts  ->  Prims.list<proposal>  ->  verdicts = (fun ( base1  :  TreeOps.tree ) ( st  :  verdicts ) ( ps  :  Prims.list<proposal> ) -> (match (ps) with
| [] -> begin
     st
     end
| (p)::r -> begin
     (fold_step base1 (step base1 st p) r)
     end))


let rec interfering_ids : DagFold.footprint  ->  Prims.list<proposal>  ->  Prims.list<Prims.int> = (fun ( fp  :  DagFold.footprint ) ( acc  :  Prims.list<proposal> ) -> (match (acc) with
| [] -> begin
     []
     end
| (a)::r -> begin
      
if (DagFold.independent fp (fp_of a)) then begin
     (interfering_ids fp r)
     end else begin
     (a.pid)::(interfering_ids fp r)
     end
     end))


let recite : Prims.list<proposal>  ->  (proposal * arb_rejection)  ->  (proposal * arb_rejection) = (fun ( accepted  :  Prims.list<proposal> ) ( pr  :  (proposal * arb_rejection) ) -> (match (pr) with
| (p, Inapplicable (i, e)) -> begin
     ((p), (Inapplicable (i, e)))
     end
| (p, Conflicts (uu___)) -> begin
     ((p), (Conflicts ((interfering_ids (fp_of p) accepted))))
     end))


let rec recite_all : Prims.list<proposal>  ->  Prims.list<(proposal * arb_rejection)>  ->  Prims.list<(proposal * arb_rejection)> = (fun ( accepted  :  Prims.list<proposal> ) ( l  :  Prims.list<(proposal * arb_rejection)> ) -> (match (l) with
| [] -> begin
     []
     end
| (pr)::r -> begin
     ((recite accepted pr))::(recite_all accepted r)
     end))


let rec collect_scripts : Prims.list<proposal>  ->  Prims.list<TreeOps.op> = (fun ( ps  :  Prims.list<proposal> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::r -> begin
     (DagFold.app p.script (collect_scripts r))
     end))


let arbitrate_pinned : TreeOps.tree  ->  Prims.list<proposal>  ->  arbitration = (fun ( base1  :  TreeOps.tree ) ( pinned  :  Prims.list<proposal> ) -> (match ((fold_step base1 (([]), ([])) pinned)) with
| (acc_rev, rej_rev) -> begin
     (

let accepted = (DagFold.rev acc_rev)
in {accepted = accepted; merged = (collect_scripts accepted); rejected = (recite_all accepted (DagFold.rev rej_rev))})
     end))


let arbitrate : TreeOps.tree  ->  Prims.list<proposal>  ->  arbitration = (fun ( base1  :  TreeOps.tree ) ( ps  :  Prims.list<proposal> ) -> (arbitrate_pinned base1 (pin ps)))


let rec pairwise_independent : Prims.list<proposal>  ->  Prims.bool = (fun ( l  :  Prims.list<proposal> ) -> (match (l) with
| [] -> begin
     true
     end
| (p)::r -> begin
     ((all_independent (fp_of p) r) && (pairwise_independent r))
     end))


let rec all_applicable : TreeOps.tree  ->  Prims.list<proposal>  ->  Prims.bool = (fun ( base1  :  TreeOps.tree ) ( l  :  Prims.list<proposal> ) -> (match (l) with
| [] -> begin
     true
     end
| (p)::r -> begin
     ((match ((can_script (Prims.parse_int "0") p.script base1)) with
| DagFold.Ok (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_applicable base1 r))
     end))


let provisional : TreeOps.tree  ->  Prims.list<proposal>  ->  (proposal * arb_rejection)  ->  Prims.bool = (fun ( base1  :  TreeOps.tree ) ( acc  :  Prims.list<proposal> ) ( pr  :  (proposal * arb_rejection) ) -> (match (pr) with
| (p, Inapplicable (i, e)) -> begin
     (Prims.op_Equals (can_script (Prims.parse_int "0") p.script base1) (DagFold.Error (((i), (e)))))
     end
| (p, Conflicts (uu___)) -> begin
     ((match ((can_script (Prims.parse_int "0") p.script base1)) with
| DagFold.Ok (_0) -> begin
     true
     end
| uu___1 -> begin
     false
     end) && (not ((all_independent (fp_of p) acc))))
     end))


let rec all_provisional : TreeOps.tree  ->  Prims.list<proposal>  ->  Prims.list<(proposal * arb_rejection)>  ->  Prims.bool = (fun ( base1  :  TreeOps.tree ) ( acc  :  Prims.list<proposal> ) ( l  :  Prims.list<(proposal * arb_rejection)> ) -> (match (l) with
| [] -> begin
     true
     end
| (pr)::r -> begin
     ((provisional base1 acc pr) && (all_provisional base1 acc r))
     end))


let justified : TreeOps.tree  ->  Prims.list<proposal>  ->  (proposal * arb_rejection)  ->  Prims.bool = (fun ( base1  :  TreeOps.tree ) ( accepted  :  Prims.list<proposal> ) ( pr  :  (proposal * arb_rejection) ) -> (match (pr) with
| (p, Inapplicable (i, e)) -> begin
     (Prims.op_Equals (can_script (Prims.parse_int "0") p.script base1) (DagFold.Error (((i), (e)))))
     end
| (p, Conflicts (ids)) -> begin
     ((((match ((can_script (Prims.parse_int "0") p.script base1)) with
| DagFold.Ok (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (not ((all_independent (fp_of p) accepted)))) && (Prims.op_Equals ids (interfering_ids (fp_of p) accepted))) && (not ((DagFold.is_empty ids))))
     end))


let rec all_justified : TreeOps.tree  ->  Prims.list<proposal>  ->  Prims.list<(proposal * arb_rejection)>  ->  Prims.bool = (fun ( base1  :  TreeOps.tree ) ( accepted  :  Prims.list<proposal> ) ( l  :  Prims.list<(proposal * arb_rejection)> ) -> (match (l) with
| [] -> begin
     true
     end
| (pr)::r -> begin
     ((justified base1 accepted pr) && (all_justified base1 accepted r))
     end))


let rec interferer : Prims.int  ->  DagFold.footprint  ->  Prims.list<proposal>  ->  FStar_Pervasives_Native.option<proposal> = (fun ( id  :  Prims.int ) ( fp  :  DagFold.footprint ) ( acc  :  Prims.list<proposal> ) -> (match (acc) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (a)::r -> begin
      
if ((Prims.op_Equals a.pid id) && (not ((DagFold.independent fp (fp_of a))))) then begin
     FStar_Pervasives_Native.Some (a)
     end else begin
     (interferer id fp r)
     end
     end))


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let rec pids : Prims.list<proposal>  ->  Prims.list<Prims.int> = (fun ( l  :  Prims.list<proposal> ) -> (match (l) with
| [] -> begin
     []
     end
| (p)::r -> begin
     (p.pid)::(pids r)
     end))


let distinct_ids : Prims.list<proposal>  ->  Prims.bool = (fun ( ps  :  Prims.list<proposal> ) -> (DagFold.distinct (pids ps)))


let rec sorted_ids : Prims.list<proposal>  ->  Prims.bool = (fun ( l  :  Prims.list<proposal> ) -> (match (l) with
| [] -> begin
     true
     end
| (uu___)::[] -> begin
     true
     end
| (a)::(b)::r -> begin
     ((a.pid <= b.pid) && (sorted_ids ((b)::r)))
     end))


let dup_base : TreeOps.tree = TreeOps.TNode ("root", "doc", [])


let dup_a : proposal = {pid = (Prims.parse_int "1"); holder = "a"; script = (TreeOps.InsertChild ("root", TreeOps.TNode ("x", "para", [])))::[]}


let dup_b : proposal = {pid = (Prims.parse_int "1"); holder = "b"; script = (TreeOps.InsertChild ("root", TreeOps.TNode ("y", "para", [])))::[]}


let mx_base : TreeOps.tree = TreeOps.TNode ("root", "doc", (TreeOps.TNode ("a", "sec", []))::(TreeOps.TNode ("b", "sec", []))::[])


let mx_both : Prims.list<TreeOps.op> = (TreeOps.InsertChild ("a", TreeOps.TNode ("n1", "para", [])))::(TreeOps.InsertChild ("b", TreeOps.TNode ("n2", "para", [])))::[]


let mx_1 : proposal = {pid = (Prims.parse_int "1"); holder = "one"; script = mx_both}


let mx_2 : proposal = {pid = (Prims.parse_int "2"); holder = "two"; script = (TreeOps.InsertChild ("a", TreeOps.TNode ("n3", "para", [])))::[]}


let mx_3 : proposal = {pid = (Prims.parse_int "3"); holder = "three"; script = (TreeOps.InsertChild ("b", TreeOps.TNode ("n4", "para", [])))::[]}


let mx_1_last : proposal = {pid = (Prims.parse_int "4"); holder = "one"; script = mx_both}




