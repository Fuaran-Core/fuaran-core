module Chain

let rec mem = (fun ( x  :  'a ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals x y) || (mem x t))
     end))

type found<'a> =
| Missing
| Found of 'a


let uu___is_Missing = (fun ( projectee  :  found<'a> ) -> (match (projectee) with
| Missing -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Found = (fun ( projectee  :  found<'a> ) -> (match (projectee) with
| Found (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Found__item___0 = (fun ( projectee  :  found<'a> ) -> (match (projectee) with
| Found (_0) -> begin
     _0
     end))


let rec insert : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( x  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     (x)::[]
     end
| (y)::t -> begin
      
if (le x y) then begin
     (x)::(y)::t
     end else begin
     (y)::(insert le x t)
     end
     end))


let rec isort : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (insert le x (isort le t))
     end))


let rec join_comma : Prims.list<Prims.string>  ->  Prims.string = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     ""
     end
| (x)::[] -> begin
     x
     end
| (x)::t -> begin
     (Prims.strcat x (Prims.strcat "," (join_comma t)))
     end))


let node_hash = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( parents  :  Prims.list<Prims.string> ) ( actor  :  Prims.string ) ( o  :  'op ) -> (h (join_comma (isort le parents)) (Prims.strcat actor (Prims.strcat "|" (enc_op o)))))




type dnode<'op> = {dparents : Prims.list<Prims.string>; dactor : Prims.string; dop : 'op}


let __proj__Mkdnode__item__dparents = (fun ( projectee  :  dnode<'op> ) -> (match (projectee) with
| {dparents = dparents; dactor = dactor; dop = dop} -> begin
     dparents
     end))


let __proj__Mkdnode__item__dactor = (fun ( projectee  :  dnode<'op> ) -> (match (projectee) with
| {dparents = dparents; dactor = dactor; dop = dop} -> begin
     dactor
     end))


let __proj__Mkdnode__item__dop = (fun ( projectee  :  dnode<'op> ) -> (match (projectee) with
| {dparents = dparents; dactor = dactor; dop = dop} -> begin
     dop
     end))

type entry<'op> = {ekey : Prims.string; enode : dnode<'op>}


let __proj__Mkentry__item__ekey = (fun ( projectee  :  entry<'op> ) -> (match (projectee) with
| {ekey = ekey; enode = enode} -> begin
     ekey
     end))


let __proj__Mkentry__item__enode = (fun ( projectee  :  entry<'op> ) -> (match (projectee) with
| {ekey = ekey; enode = enode} -> begin
     enode
     end))


let rec keys_of = (fun ( es  :  Prims.list<entry<'op>> ) -> (match (es) with
| [] -> begin
     []
     end
| (e)::t -> begin
     (e.ekey)::(keys_of t)
     end))


let rec lookup_node = (fun ( es  :  Prims.list<entry<'op>> ) ( k  :  Prims.string ) -> (match (es) with
| [] -> begin
     Missing
     end
| (e)::t -> begin
      
if (Prims.op_Equals e.ekey k) then begin
     Found (e.enode)
     end else begin
     (lookup_node t k)
     end
     end))


let rec first_absent : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  found<Prims.string> = (fun ( keys  :  Prims.list<Prims.string> ) ( ps  :  Prims.list<Prims.string> ) -> (match (ps) with
| [] -> begin
     Missing
     end
| (p)::t -> begin
      
if (mem p keys) then begin
     (first_absent keys t)
     end else begin
     Found (p)
     end
     end))

type dbreak = {bnode : Prims.string; breason : Prims.string; bexpected : Prims.string; bgot : Prims.string}


let __proj__Mkdbreak__item__bnode : dbreak  ->  Prims.string = (fun ( projectee  :  dbreak ) -> (match (projectee) with
| {bnode = bnode; breason = breason; bexpected = bexpected; bgot = bgot} -> begin
     bnode
     end))


let __proj__Mkdbreak__item__breason : dbreak  ->  Prims.string = (fun ( projectee  :  dbreak ) -> (match (projectee) with
| {bnode = bnode; breason = breason; bexpected = bexpected; bgot = bgot} -> begin
     breason
     end))


let __proj__Mkdbreak__item__bexpected : dbreak  ->  Prims.string = (fun ( projectee  :  dbreak ) -> (match (projectee) with
| {bnode = bnode; breason = breason; bexpected = bexpected; bgot = bgot} -> begin
     bexpected
     end))


let __proj__Mkdbreak__item__bgot : dbreak  ->  Prims.string = (fun ( projectee  :  dbreak ) -> (match (projectee) with
| {bnode = bnode; breason = breason; bexpected = bexpected; bgot = bgot} -> begin
     bgot
     end))


let break_content : Prims.string  ->  Prims.string  ->  dbreak = (fun ( key  :  Prims.string ) ( recomputed  :  Prims.string ) -> {bnode = key; breason = "content-id mismatch (tampered node)"; bexpected = recomputed; bgot = key})


let break_parent : Prims.string  ->  Prims.string  ->  dbreak = (fun ( key  :  Prims.string ) ( missing  :  Prims.string ) -> {bnode = key; breason = "missing parent"; bexpected = ""; bgot = missing})


let rec first_break_from = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( keys  :  Prims.list<Prims.string> ) ( es  :  Prims.list<entry<'op>> ) -> (match (es) with
| [] -> begin
     Missing
     end
| (e)::t -> begin
     (

let recomputed = (node_hash h enc_op le e.enode.dparents e.enode.dactor e.enode.dop)
in  
if (not ((Prims.op_Equals e.ekey recomputed))) then begin
     Found ((break_content e.ekey recomputed))
     end else begin
     (match ((first_absent keys e.enode.dparents)) with
| Found (p) -> begin
     Found ((break_parent e.ekey p))
     end
| Missing -> begin
     (first_break_from h enc_op le keys t)
     end)
     end)
     end))


let first_break = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( es  :  Prims.list<entry<'op>> ) -> (first_break_from h enc_op le (keys_of es) es))


let verify_dag = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( es  :  Prims.list<entry<'op>> ) -> (match ((first_break h enc_op le es)) with
| Missing -> begin
     true
     end
| Found (uu___) -> begin
     false
     end))


let entry_ok = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( e  :  entry<'op> ) -> (Prims.op_Equals e.ekey (node_hash h enc_op le e.enode.dparents e.enode.dactor e.enode.dop)))


let rec all_ok = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( es  :  Prims.list<entry<'op>> ) -> (match (es) with
| [] -> begin
     true
     end
| (e)::t -> begin
     ((entry_ok h enc_op le e) && (all_ok h enc_op le t))
     end))


let rec parents_present = (fun ( keys  :  Prims.list<Prims.string> ) ( es  :  Prims.list<entry<'op>> ) -> (match (es) with
| [] -> begin
     true
     end
| (e)::t -> begin
     (match ((first_absent keys e.enode.dparents)) with
| Found (uu___) -> begin
     false
     end
| Missing -> begin
     (parents_present keys t)
     end)
     end))

type step<'op> =
| SAppend of Prims.string * Prims.string * 'op
| SMerge of Prims.string * Prims.string * Prims.string * 'op


let uu___is_SAppend = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SAppend (parent, actor, o) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SAppend__item__parent = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SAppend (parent, actor, o) -> begin
     parent
     end))


let __proj__SAppend__item__actor = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SAppend (parent, actor, o) -> begin
     actor
     end))


let __proj__SAppend__item__o = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SAppend (parent, actor, o) -> begin
     o
     end))


let uu___is_SMerge = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SMerge (left, right, actor, o) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SMerge__item__left = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SMerge (left, right, actor, o) -> begin
     left
     end))


let __proj__SMerge__item__right = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SMerge (left, right, actor, o) -> begin
     right
     end))


let __proj__SMerge__item__actor = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SMerge (left, right, actor, o) -> begin
     actor
     end))


let __proj__SMerge__item__o = (fun ( projectee  :  step<'op> ) -> (match (projectee) with
| SMerge (left, right, actor, o) -> begin
     o
     end))


let step_parents = (fun ( s  :  step<'op> ) -> (match (s) with
| SAppend (p, uu___, uu___1) -> begin
      
if (Prims.op_Equals p "") then begin
     []
     end else begin
     (p)::[]
     end
     end
| SMerge (l, r, uu___, uu___1) -> begin
     (l)::(r)::[]
     end))


let step_actor = (fun ( s  :  step<'op> ) -> (match (s) with
| SAppend (uu___, a, uu___1) -> begin
     a
     end
| SMerge (uu___, uu___1, a, uu___2) -> begin
     a
     end))


let step_op = (fun ( s  :  step<'op> ) -> (match (s) with
| SAppend (uu___, uu___1, o) -> begin
     o
     end
| SMerge (uu___, uu___1, uu___2, o) -> begin
     o
     end))


let rec upsert = (fun ( es  :  Prims.list<entry<'op>> ) ( e  :  entry<'op> ) -> (match (es) with
| [] -> begin
     (e)::[]
     end
| (x)::t -> begin
      
if (Prims.op_Equals x.ekey e.ekey) then begin
     (e)::t
     end else begin
     (x)::(upsert t e)
     end
     end))


let apply_step = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( es  :  Prims.list<entry<'op>> ) ( s  :  step<'op> ) -> (

let ps = (step_parents s)
in (

let a = (step_actor s)
in (

let o = (step_op s)
in (upsert es {ekey = (node_hash h enc_op le ps a o); enode = {dparents = ps; dactor = a; dop = o}})))))


let rec build = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( es  :  Prims.list<entry<'op>> ) ( ss  :  Prims.list<step<'op>> ) -> (match (ss) with
| [] -> begin
     es
     end
| (s)::t -> begin
     (build h enc_op le (apply_step h enc_op le es s) t)
     end))


let rec steps_well_parented = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( es  :  Prims.list<entry<'op>> ) ( ss  :  Prims.list<step<'op>> ) -> (match (ss) with
| [] -> begin
     true
     end
| (s)::t -> begin
     (match ((first_absent (keys_of es) (step_parents s))) with
| Found (uu___) -> begin
     false
     end
| Missing -> begin
     (steps_well_parented h enc_op le (apply_step h enc_op le es s) t)
     end)
     end))


let same_preimage = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( n  :  dnode<'op> ) ( n'  :  dnode<'op> ) -> (((Prims.op_Equals (isort le n.dparents) (isort le n'.dparents)) && (Prims.op_Equals n.dactor n'.dactor)) && (Prims.op_Equals n.dop n'.dop)))


let rec retarget = (fun ( es  :  Prims.list<entry<'op>> ) ( k  :  Prims.string ) ( n'  :  dnode<'op> ) -> (match (es) with
| [] -> begin
     []
     end
| (e)::t -> begin
      
if (Prims.op_Equals e.ekey k) then begin
     ({ekey = e.ekey; enode = n'})::t
     end else begin
     (e)::(retarget t k n')
     end
     end))


let rec sorted : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (uu___)::[] -> begin
     true
     end
| (x)::(y)::t -> begin
     ((le x y) && (sorted le ((y)::t)))
     end))


let rec no_dup : Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((not ((mem x t))) && (no_dup t))
     end))


let rec dedup : Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (mem x t) then begin
     (dedup t)
     end else begin
     (x)::(dedup t)
     end
     end))


let merge_all_parents : (Prims.string  ->  Prims.string  ->  Prims.bool)  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( heads  :  Prims.list<Prims.string> ) -> (isort le (dedup heads)))


let merge_all_id = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( le  :  Prims.string  ->  Prims.string  ->  Prims.bool ) ( heads  :  Prims.list<Prims.string> ) ( actor  :  Prims.string ) ( o  :  'op ) -> (node_hash h enc_op le (merge_all_parents le heads) actor o))

type pos =
| PZero
| PSucc of pos


let uu___is_PZero : pos  ->  Prims.bool = (fun ( projectee  :  pos ) -> (match (projectee) with
| PZero -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_PSucc : pos  ->  Prims.bool = (fun ( projectee  :  pos ) -> (match (projectee) with
| PSucc (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PSucc__item___0 : pos  ->  pos = (fun ( projectee  :  pos ) -> (match (projectee) with
| PSucc (_0) -> begin
     _0
     end))

type record<'op> = {rseq : pos; ractor : Prims.string; rop : 'op; rprev : Prims.string; rhash : Prims.string}


let __proj__Mkrecord__item__rseq = (fun ( projectee  :  record<'op> ) -> (match (projectee) with
| {rseq = rseq; ractor = ractor; rop = rop; rprev = rprev; rhash = rhash} -> begin
     rseq
     end))


let __proj__Mkrecord__item__ractor = (fun ( projectee  :  record<'op> ) -> (match (projectee) with
| {rseq = rseq; ractor = ractor; rop = rop; rprev = rprev; rhash = rhash} -> begin
     ractor
     end))


let __proj__Mkrecord__item__rop = (fun ( projectee  :  record<'op> ) -> (match (projectee) with
| {rseq = rseq; ractor = ractor; rop = rop; rprev = rprev; rhash = rhash} -> begin
     rop
     end))


let __proj__Mkrecord__item__rprev = (fun ( projectee  :  record<'op> ) -> (match (projectee) with
| {rseq = rseq; ractor = ractor; rop = rop; rprev = rprev; rhash = rhash} -> begin
     rprev
     end))


let __proj__Mkrecord__item__rhash = (fun ( projectee  :  record<'op> ) -> (match (projectee) with
| {rseq = rseq; ractor = ractor; rop = rop; rprev = rprev; rhash = rhash} -> begin
     rhash
     end))


let rec_payload = (fun ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( s  :  pos ) ( a  :  Prims.string ) ( o  :  'op ) -> (Prims.strcat "{\"seq\":" (Prims.strcat (show s) (Prims.strcat ",\"actor\":" (Prims.strcat a (Prims.strcat ",\"op\":" (Prims.strcat (enc_op o) "}")))))))


let rec_hash = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( prev  :  Prims.string ) ( s  :  pos ) ( a  :  Prims.string ) ( o  :  'op ) -> (h prev (rec_payload show enc_op s a o)))




type cbreak = {cindex : pos; creason : Prims.string; cexpected : Prims.string; cgot : Prims.string}


let __proj__Mkcbreak__item__cindex : cbreak  ->  pos = (fun ( projectee  :  cbreak ) -> (match (projectee) with
| {cindex = cindex; creason = creason; cexpected = cexpected; cgot = cgot} -> begin
     cindex
     end))


let __proj__Mkcbreak__item__creason : cbreak  ->  Prims.string = (fun ( projectee  :  cbreak ) -> (match (projectee) with
| {cindex = cindex; creason = creason; cexpected = cexpected; cgot = cgot} -> begin
     creason
     end))


let __proj__Mkcbreak__item__cexpected : cbreak  ->  Prims.string = (fun ( projectee  :  cbreak ) -> (match (projectee) with
| {cindex = cindex; creason = creason; cexpected = cexpected; cgot = cgot} -> begin
     cexpected
     end))


let __proj__Mkcbreak__item__cgot : cbreak  ->  Prims.string = (fun ( projectee  :  cbreak ) -> (match (projectee) with
| {cindex = cindex; creason = creason; cexpected = cexpected; cgot = cgot} -> begin
     cgot
     end))


let rec first_chain_break_from = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( prev  :  Prims.string ) ( i  :  pos ) ( rs  :  Prims.list<record<'op>> ) -> (match (rs) with
| [] -> begin
     Missing
     end
| (r)::rest -> begin
      
if (not ((Prims.op_Equals r.rseq i))) then begin
     Found ({cindex = i; creason = "sequence-number mismatch"; cexpected = (show i); cgot = (show r.rseq)})
     end else begin
      
if (not ((Prims.op_Equals r.rprev prev))) then begin
     Found ({cindex = i; creason = "prev-hash link broken"; cexpected = prev; cgot = r.rprev})
     end else begin
     (

let expected = (rec_hash h show enc_op prev r.rseq r.ractor r.rop)
in  
if (not ((Prims.op_Equals r.rhash expected))) then begin
     Found ({cindex = i; creason = "hash mismatch (tampered op/actor/seq)"; cexpected = expected; cgot = r.rhash})
     end else begin
     (first_chain_break_from h show enc_op r.rhash (PSucc (i)) rest)
     end)
     end
     end
     end))


let first_chain_break = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( genesis  :  Prims.string ) ( rs  :  Prims.list<record<'op>> ) -> (first_chain_break_from h show enc_op genesis PZero rs))


let verify_chain = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( genesis  :  Prims.string ) ( rs  :  Prims.list<record<'op>> ) -> (match ((first_chain_break h show enc_op genesis rs)) with
| Missing -> begin
     true
     end
| Found (uu___) -> begin
     false
     end))


let rec chain_ok_from = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( prev  :  Prims.string ) ( i  :  pos ) ( rs  :  Prims.list<record<'op>> ) -> (match (rs) with
| [] -> begin
     true
     end
| (r)::rest -> begin
     ((((Prims.op_Equals r.rseq i) && (Prims.op_Equals r.rprev prev)) && (Prims.op_Equals r.rhash (rec_hash h show enc_op prev r.rseq r.ractor r.rop))) && (chain_ok_from h show enc_op r.rhash (PSucc (i)) rest))
     end))

type cstep<'op> = {cactor : Prims.string; cop : 'op}


let __proj__Mkcstep__item__cactor = (fun ( projectee  :  cstep<'op> ) -> (match (projectee) with
| {cactor = cactor; cop = cop} -> begin
     cactor
     end))


let __proj__Mkcstep__item__cop = (fun ( projectee  :  cstep<'op> ) -> (match (projectee) with
| {cactor = cactor; cop = cop} -> begin
     cop
     end))


let append_rec = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( prev  :  Prims.string ) ( i  :  pos ) ( a  :  Prims.string ) ( o  :  'op ) -> {rseq = i; ractor = a; rop = o; rprev = prev; rhash = (rec_hash h show enc_op prev i a o)})


let rec build_chain = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( prev  :  Prims.string ) ( i  :  pos ) ( cs  :  Prims.list<cstep<'op>> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (

let r = (append_rec h show enc_op prev i c.cactor c.cop)
in (r)::(build_chain h show enc_op r.rhash (PSucc (i)) t))
     end))


let rec record_at = (fun ( rs  :  Prims.list<record<'op>> ) ( n  :  pos ) -> (match (((rs), (n))) with
| ([], uu___) -> begin
     Missing
     end
| ((r)::uu___, PZero) -> begin
     Found (r)
     end
| ((uu___)::t, PSucc (m)) -> begin
     (record_at t m)
     end))


let rec replace_at = (fun ( rs  :  Prims.list<record<'op>> ) ( n  :  pos ) ( r'  :  record<'op> ) -> (match (((rs), (n))) with
| ([], uu___) -> begin
     []
     end
| ((uu___)::t, PZero) -> begin
     (r')::t
     end
| ((r)::t, PSucc (m)) -> begin
     (r)::(replace_at t m r')
     end))


let same_content = (fun ( r  :  record<'op> ) ( r'  :  record<'op> ) -> (((Prims.op_Equals r.rseq r'.rseq) && (Prims.op_Equals r.ractor r'.ractor)) && (Prims.op_Equals r.rop r'.rop)))

type applied<'st, 'rej> =
| Applied of 'st
| Refused of 'rej


let uu___is_Applied = (fun ( projectee  :  applied<'st, 'rej> ) -> (match (projectee) with
| Applied (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Applied__item___0 = (fun ( projectee  :  applied<'st, 'rej> ) -> (match (projectee) with
| Applied (_0) -> begin
     _0
     end))


let uu___is_Refused = (fun ( projectee  :  applied<'st, 'rej> ) -> (match (projectee) with
| Refused (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Refused__item___0 = (fun ( projectee  :  applied<'st, 'rej> ) -> (match (projectee) with
| Refused (_0) -> begin
     _0
     end))

type replayed<'st, 'rej> =
| Replayed of 'st
| Halted of pos * 'rej


let uu___is_Replayed = (fun ( projectee  :  replayed<'st, 'rej> ) -> (match (projectee) with
| Replayed (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Replayed__item___0 = (fun ( projectee  :  replayed<'st, 'rej> ) -> (match (projectee) with
| Replayed (_0) -> begin
     _0
     end))


let uu___is_Halted = (fun ( projectee  :  replayed<'st, 'rej> ) -> (match (projectee) with
| Halted (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Halted__item___0 = (fun ( projectee  :  replayed<'st, 'rej> ) -> (match (projectee) with
| Halted (_0, _1) -> begin
     _0
     end))


let __proj__Halted__item___1 = (fun ( projectee  :  replayed<'st, 'rej> ) -> (match (projectee) with
| Halted (_0, _1) -> begin
     _1
     end))


let rec replay_go = (fun ( apply  :  'op  ->  'st  ->  applied<'st, 'rej> ) ( i  :  pos ) ( s  :  'st ) ( rs  :  Prims.list<record<'op>> ) -> (match (rs) with
| [] -> begin
     Replayed (s)
     end
| (r)::rest -> begin
     (match ((apply r.rop s)) with
| Applied (s') -> begin
     (replay_go apply (PSucc (i)) s' rest)
     end
| Refused (e) -> begin
     Halted (i, e)
     end)
     end))


let replay = (fun ( apply  :  'op  ->  'st  ->  applied<'st, 'rej> ) ( s0  :  'st ) ( rs  :  Prims.list<record<'op>> ) -> (replay_go apply PZero s0 rs))


let rec take = (fun ( n  :  pos ) ( l  :  Prims.list<'a> ) -> (match (((n), (l))) with
| (PZero, uu___) -> begin
     []
     end
| (PSucc (uu___), []) -> begin
     []
     end
| (PSucc (m), (x)::t) -> begin
     (x)::(take m t)
     end))


let rec drop = (fun ( n  :  pos ) ( l  :  Prims.list<'a> ) -> (match (((n), (l))) with
| (PZero, uu___) -> begin
     l
     end
| (PSucc (uu___), []) -> begin
     []
     end
| (PSucc (m), (uu___)::t) -> begin
     (drop m t)
     end))


let rec within = (fun ( n  :  pos ) ( l  :  Prims.list<'a> ) -> (match (((n), (l))) with
| (PZero, uu___) -> begin
     true
     end
| (PSucc (uu___), []) -> begin
     false
     end
| (PSucc (m), (uu___)::t) -> begin
     (within m t)
     end))


let rec hash_at_boundary = (fun ( prev  :  Prims.string ) ( n  :  pos ) ( rs  :  Prims.list<record<'op>> ) -> (match (((n), (rs))) with
| (PZero, uu___) -> begin
     prev
     end
| (PSucc (uu___), []) -> begin
     prev
     end
| (PSucc (m), (r)::t) -> begin
     (hash_at_boundary r.rhash m t)
     end))

type snapshot<'st> = {sseq : pos; sstate : 'st; sprev : Prims.string; shash : Prims.string}


let __proj__Mksnapshot__item__sseq = (fun ( projectee  :  snapshot<'st> ) -> (match (projectee) with
| {sseq = sseq; sstate = sstate; sprev = sprev; shash = shash} -> begin
     sseq
     end))


let __proj__Mksnapshot__item__sstate = (fun ( projectee  :  snapshot<'st> ) -> (match (projectee) with
| {sseq = sseq; sstate = sstate; sprev = sprev; shash = shash} -> begin
     sstate
     end))


let __proj__Mksnapshot__item__sprev = (fun ( projectee  :  snapshot<'st> ) -> (match (projectee) with
| {sseq = sseq; sstate = sstate; sprev = sprev; shash = shash} -> begin
     sprev
     end))


let __proj__Mksnapshot__item__shash = (fun ( projectee  :  snapshot<'st> ) -> (match (projectee) with
| {sseq = sseq; sstate = sstate; sprev = sprev; shash = shash} -> begin
     shash
     end))


let snap_payload = (fun ( show  :  pos  ->  Prims.string ) ( enc_state  :  'st  ->  Prims.string ) ( n  :  pos ) ( s  :  'st ) -> (Prims.strcat "{\"snapshot\":true,\"seq\":" (Prims.strcat (show n) (Prims.strcat ",\"state\":" (Prims.strcat (enc_state s) "}")))))


let snap_payload_chain_only = (fun ( show  :  pos  ->  Prims.string ) ( n  :  pos ) ( uu___  :  'st ) -> (Prims.strcat "{\"snapshot\":true,\"seq\":" (Prims.strcat (show n) ",\"stateHashed\":false}")))

type compacted<'op, 'st> =
| CompactRefused of Prims.string
| Compacted of snapshot<'st> * Prims.list<record<'op>>


let uu___is_CompactRefused = (fun ( projectee  :  compacted<'op, 'st> ) -> (match (projectee) with
| CompactRefused (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CompactRefused__item___0 = (fun ( projectee  :  compacted<'op, 'st> ) -> (match (projectee) with
| CompactRefused (_0) -> begin
     _0
     end))


let uu___is_Compacted = (fun ( projectee  :  compacted<'op, 'st> ) -> (match (projectee) with
| Compacted (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Compacted__item___0 = (fun ( projectee  :  compacted<'op, 'st> ) -> (match (projectee) with
| Compacted (_0, _1) -> begin
     _0
     end))


let __proj__Compacted__item___1 = (fun ( projectee  :  compacted<'op, 'st> ) -> (match (projectee) with
| Compacted (_0, _1) -> begin
     _1
     end))


let compact = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( pay  :  pos  ->  'st  ->  Prims.string ) ( apply  :  'op  ->  'st  ->  applied<'st, 'rej> ) ( genesis  :  Prims.string ) ( s0  :  'st ) ( rs  :  Prims.list<record<'op>> ) ( n  :  pos ) ->  
if (not ((within n rs))) then begin
     CompactRefused ("OpStream.snapshotAt: seq out of range")
     end else begin
     (match ((replay apply s0 (take n rs))) with
| Halted (i, uu___) -> begin
     CompactRefused ((Prims.strcat "OpStream.snapshotAt: prefix replay failed at " (show i)))
     end
| Replayed (s) -> begin
     (

let prev = (hash_at_boundary genesis n rs)
in Compacted ({sseq = n; sstate = s; sprev = prev; shash = (h prev (pay n s))}, (drop n rs)))
     end)
     end)


let replay_from = (fun ( apply  :  'op  ->  'st  ->  applied<'st, 'rej> ) ( snap  :  snapshot<'st> ) ( tail  :  Prims.list<record<'op>> ) -> (replay apply snap.sstate tail))


let verify_across = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( pay  :  pos  ->  'st  ->  Prims.string ) ( snap  :  snapshot<'st> ) ( tail  :  Prims.list<record<'op>> ) -> ((Prims.op_Equals snap.shash (h snap.sprev (pay snap.sseq snap.sstate))) && (chain_ok_from h show enc_op snap.sprev snap.sseq tail)))


let rec padd : pos  ->  pos  ->  pos = (fun ( i  :  pos ) ( n  :  pos ) -> (match (n) with
| PZero -> begin
     i
     end
| PSucc (m) -> begin
     PSucc ((padd i m))
     end))


let offset = (fun ( n  :  pos ) ( r  :  replayed<'st, 'rej> ) -> (match (r) with
| Replayed (s) -> begin
     Replayed (s)
     end
| Halted (j, e) -> begin
     Halted ((padd n j), e)
     end))


let rec plen = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     PZero
     end
| (uu___)::t -> begin
     PSucc ((plen t))
     end))


let rec snoc = (fun ( l  :  Prims.list<'a> ) ( x  :  'a ) -> (match (l) with
| [] -> begin
     (x)::[]
     end
| (h)::t -> begin
     (h)::(snoc t x)
     end))


let rec last_hash = (fun ( prev  :  Prims.string ) ( rs  :  Prims.list<record<'op>> ) -> (match (rs) with
| [] -> begin
     prev
     end
| (r)::t -> begin
     (last_hash r.rhash t)
     end))


let append_full = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( genesis  :  Prims.string ) ( rs  :  Prims.list<record<'op>> ) ( a  :  Prims.string ) ( o  :  'op ) -> (snoc rs (append_rec h show enc_op (last_hash genesis rs) (plen rs) a o)))


let append_to = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( snap  :  snapshot<'st> ) ( tail  :  Prims.list<record<'op>> ) ( a  :  Prims.string ) ( o  :  'op ) -> (snoc tail (append_rec h show enc_op (last_hash snap.sprev tail) (padd snap.sseq (plen tail)) a o)))


let starts_at = (fun ( n  :  pos ) ( tail  :  Prims.list<record<'op>> ) -> (match (tail) with
| [] -> begin
     true
     end
| (r)::uu___ -> begin
     (Prims.op_Equals r.rseq n)
     end))


let compact_from = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( pay  :  pos  ->  'st  ->  Prims.string ) ( apply  :  'op  ->  'st  ->  applied<'st, 'rej> ) ( snap  :  snapshot<'st> ) ( tail  :  Prims.list<record<'op>> ) ( k  :  pos ) ->  
if (not ((starts_at snap.sseq tail))) then begin
     CompactRefused ("OpStream.snapshot: the tail does not start at the snapshot\'s boundary")
     end else begin
      
if (not ((within k tail))) then begin
     CompactRefused ("OpStream.snapshotAt: seq out of range")
     end else begin
     (match ((replay_go apply snap.sseq snap.sstate (take k tail))) with
| Halted (i, uu___) -> begin
     CompactRefused ((Prims.strcat "OpStream.snapshotAt: prefix replay failed at " (show i)))
     end
| Replayed (s) -> begin
     (

let prev = (hash_at_boundary snap.sprev k tail)
in (

let n' = (padd snap.sseq k)
in Compacted ({sseq = n'; sstate = s; sprev = prev; shash = (h prev (pay n' s))}, (drop k tail))))
     end)
     end
     end)

type kentry = {kkey : Prims.string; kseq : pos; khash : Prims.string}


let __proj__Mkkentry__item__kkey : kentry  ->  Prims.string = (fun ( projectee  :  kentry ) -> (match (projectee) with
| {kkey = kkey; kseq = kseq; khash = khash} -> begin
     kkey
     end))


let __proj__Mkkentry__item__kseq : kentry  ->  pos = (fun ( projectee  :  kentry ) -> (match (projectee) with
| {kkey = kkey; kseq = kseq; khash = khash} -> begin
     kseq
     end))


let __proj__Mkkentry__item__khash : kentry  ->  Prims.string = (fun ( projectee  :  kentry ) -> (match (projectee) with
| {kkey = kkey; kseq = kseq; khash = khash} -> begin
     khash
     end))


let rec kmem : Prims.string  ->  Prims.list<kentry>  ->  Prims.bool = (fun ( k  :  Prims.string ) ( idx  :  Prims.list<kentry> ) -> (match (idx) with
| [] -> begin
     false
     end
| (e)::t -> begin
     ((Prims.op_Equals e.kkey k) || (kmem k t))
     end))


let kadd : Prims.list<kentry>  ->  Prims.string  ->  pos  ->  Prims.string  ->  Prims.list<kentry> = (fun ( idx  :  Prims.list<kentry> ) ( k  :  Prims.string ) ( seq  :  pos ) ( hash  :  Prims.string ) ->  
if (kmem k idx) then begin
     idx
     end else begin
     (snoc idx {kkey = k; kseq = seq; khash = hash})
     end)


let rec index_onto = (fun ( key_of  :  'op  ->  found<Prims.string> ) ( idx  :  Prims.list<kentry> ) ( rs  :  Prims.list<record<'op>> ) -> (match (rs) with
| [] -> begin
     idx
     end
| (r)::t -> begin
     (

let idx' = (match ((key_of r.rop)) with
| Found (k) -> begin
     (kadd idx k r.rseq r.rhash)
     end
| Missing -> begin
     idx
     end)
in (index_onto key_of idx' t))
     end))

type capture = {pseq : pos; peff : Prims.string; pdet : Prims.string; pval : Prims.string; pprev : Prims.string; phash : Prims.string}


let __proj__Mkcapture__item__pseq : capture  ->  pos = (fun ( projectee  :  capture ) -> (match (projectee) with
| {pseq = pseq; peff = peff; pdet = pdet; pval = pval; pprev = pprev; phash = phash} -> begin
     pseq
     end))


let __proj__Mkcapture__item__peff : capture  ->  Prims.string = (fun ( projectee  :  capture ) -> (match (projectee) with
| {pseq = pseq; peff = peff; pdet = pdet; pval = pval; pprev = pprev; phash = phash} -> begin
     peff
     end))


let __proj__Mkcapture__item__pdet : capture  ->  Prims.string = (fun ( projectee  :  capture ) -> (match (projectee) with
| {pseq = pseq; peff = peff; pdet = pdet; pval = pval; pprev = pprev; phash = phash} -> begin
     pdet
     end))


let __proj__Mkcapture__item__pval : capture  ->  Prims.string = (fun ( projectee  :  capture ) -> (match (projectee) with
| {pseq = pseq; peff = peff; pdet = pdet; pval = pval; pprev = pprev; phash = phash} -> begin
     pval
     end))


let __proj__Mkcapture__item__pprev : capture  ->  Prims.string = (fun ( projectee  :  capture ) -> (match (projectee) with
| {pseq = pseq; peff = peff; pdet = pdet; pval = pval; pprev = pprev; phash = phash} -> begin
     pprev
     end))


let __proj__Mkcapture__item__phash : capture  ->  Prims.string = (fun ( projectee  :  capture ) -> (match (projectee) with
| {pseq = pseq; peff = peff; pdet = pdet; pval = pval; pprev = pprev; phash = phash} -> begin
     phash
     end))


let cap_payload : (pos  ->  Prims.string)  ->  (Prims.string  ->  Prims.string)  ->  pos  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.string = (fun ( show  :  pos  ->  Prims.string ) ( esc  :  Prims.string  ->  Prims.string ) ( s  :  pos ) ( e  :  Prims.string ) ( d  :  Prims.string ) ( v  :  Prims.string ) -> (Prims.strcat "{\"capture\":true,\"seq\":" (Prims.strcat (show s) (Prims.strcat ",\"eff\":" (Prims.strcat (esc e) (Prims.strcat ",\"det\":" (Prims.strcat (esc d) (Prims.strcat ",\"value\":" (Prims.strcat v "}")))))))))


let rec first_capture_break_from : (Prims.string  ->  Prims.string  ->  Prims.string)  ->  (pos  ->  Prims.string)  ->  (Prims.string  ->  Prims.string)  ->  Prims.string  ->  pos  ->  Prims.list<capture>  ->  found<cbreak> = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( esc  :  Prims.string  ->  Prims.string ) ( prev  :  Prims.string ) ( i  :  pos ) ( cs  :  Prims.list<capture> ) -> (match (cs) with
| [] -> begin
     Missing
     end
| (c)::rest -> begin
      
if (not ((Prims.op_Equals c.pseq i))) then begin
     Found ({cindex = i; creason = "sequence-number mismatch"; cexpected = (show i); cgot = (show c.pseq)})
     end else begin
      
if (not ((Prims.op_Equals c.pprev prev))) then begin
     Found ({cindex = i; creason = "prev-hash link broken"; cexpected = prev; cgot = c.pprev})
     end else begin
     (

let expected = (h prev (cap_payload show esc c.pseq c.peff c.pdet c.pval))
in  
if (not ((Prims.op_Equals c.phash expected))) then begin
     Found ({cindex = i; creason = "hash mismatch (tampered op/actor/seq)"; cexpected = expected; cgot = c.phash})
     end else begin
     (first_capture_break_from h show esc c.phash (PSucc (i)) rest)
     end)
     end
     end
     end))


let verify_captures : (Prims.string  ->  Prims.string  ->  Prims.string)  ->  (pos  ->  Prims.string)  ->  (Prims.string  ->  Prims.string)  ->  Prims.string  ->  Prims.list<capture>  ->  Prims.bool = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( esc  :  Prims.string  ->  Prims.string ) ( genesis  :  Prims.string ) ( cs  :  Prims.list<capture> ) -> (match ((first_capture_break_from h show esc genesis PZero cs)) with
| Missing -> begin
     true
     end
| Found (uu___) -> begin
     false
     end))

type creq = {qeff : Prims.string; qdet : Prims.string; qval : Prims.string}


let __proj__Mkcreq__item__qeff : creq  ->  Prims.string = (fun ( projectee  :  creq ) -> (match (projectee) with
| {qeff = qeff; qdet = qdet; qval = qval} -> begin
     qeff
     end))


let __proj__Mkcreq__item__qdet : creq  ->  Prims.string = (fun ( projectee  :  creq ) -> (match (projectee) with
| {qeff = qeff; qdet = qdet; qval = qval} -> begin
     qdet
     end))


let __proj__Mkcreq__item__qval : creq  ->  Prims.string = (fun ( projectee  :  creq ) -> (match (projectee) with
| {qeff = qeff; qdet = qdet; qval = qval} -> begin
     qval
     end))


let rec record_session : (Prims.string  ->  Prims.string  ->  Prims.string)  ->  (pos  ->  Prims.string)  ->  (Prims.string  ->  Prims.string)  ->  Prims.string  ->  pos  ->  Prims.list<creq>  ->  Prims.list<capture> = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( esc  :  Prims.string  ->  Prims.string ) ( prev  :  Prims.string ) ( i  :  pos ) ( qs  :  Prims.list<creq> ) -> (match (qs) with
| [] -> begin
     []
     end
| (q)::t -> begin
      
if (Prims.op_Equals q.qdet "deterministic") then begin
     (record_session h show esc prev i t)
     end else begin
     (

let c = {pseq = i; peff = q.qeff; pdet = q.qdet; pval = q.qval; pprev = prev; phash = (h prev (cap_payload show esc i q.qeff q.qdet q.qval))}
in (c)::(record_session h show esc c.phash (PSucc (i)) t))
     end
     end))

type rfault =
| RExhausted of Prims.string
| RIdentity of Prims.string * Prims.string
| RLabel of Prims.string * Prims.string
| RNotCanonical of Prims.string


let uu___is_RExhausted : rfault  ->  Prims.bool = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RExhausted (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RExhausted__item___0 : rfault  ->  Prims.string = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RExhausted (_0) -> begin
     _0
     end))


let uu___is_RIdentity : rfault  ->  Prims.bool = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RIdentity (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RIdentity__item___0 : rfault  ->  Prims.string = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RIdentity (_0, _1) -> begin
     _0
     end))


let __proj__RIdentity__item___1 : rfault  ->  Prims.string = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RIdentity (_0, _1) -> begin
     _1
     end))


let uu___is_RLabel : rfault  ->  Prims.bool = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RLabel (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RLabel__item___0 : rfault  ->  Prims.string = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RLabel (_0, _1) -> begin
     _0
     end))


let __proj__RLabel__item___1 : rfault  ->  Prims.string = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RLabel (_0, _1) -> begin
     _1
     end))


let uu___is_RNotCanonical : rfault  ->  Prims.bool = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RNotCanonical (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RNotCanonical__item___0 : rfault  ->  Prims.string = (fun ( projectee  :  rfault ) -> (match (projectee) with
| RNotCanonical (_0) -> begin
     _0
     end))

type rstep =
| RValue of Prims.string * Prims.list<capture>
| RFault of rfault


let uu___is_RValue : rstep  ->  Prims.bool = (fun ( projectee  :  rstep ) -> (match (projectee) with
| RValue (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RValue__item___0 : rstep  ->  Prims.string = (fun ( projectee  :  rstep ) -> (match (projectee) with
| RValue (_0, _1) -> begin
     _0
     end))


let __proj__RValue__item___1 : rstep  ->  Prims.list<capture> = (fun ( projectee  :  rstep ) -> (match (projectee) with
| RValue (_0, _1) -> begin
     _1
     end))


let uu___is_RFault : rstep  ->  Prims.bool = (fun ( projectee  :  rstep ) -> (match (projectee) with
| RFault (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RFault__item___0 : rstep  ->  rfault = (fun ( projectee  :  rstep ) -> (match (projectee) with
| RFault (_0) -> begin
     _0
     end))


let replay_strict : (Prims.string  ->  Prims.bool)  ->  creq  ->  Prims.list<capture>  ->  rstep = (fun ( canonical  :  Prims.string  ->  Prims.bool ) ( q  :  creq ) ( cs  :  Prims.list<capture> ) ->  
if (not ((canonical q.qdet))) then begin
     RFault (RNotCanonical (q.qdet))
     end else begin
      
if (Prims.op_Equals q.qdet "deterministic") then begin
     RValue (q.qval, cs)
     end else begin
     (match (cs) with
| [] -> begin
     RFault (RExhausted (q.qeff))
     end
| (c)::rest -> begin
      
if (not ((Prims.op_Equals c.peff q.qeff))) then begin
     RFault (RIdentity (q.qeff, c.peff))
     end else begin
      
if (not ((Prims.op_Equals c.pdet q.qdet))) then begin
     RFault (RLabel (q.qdet, c.pdet))
     end else begin
     RValue (c.pval, rest)
     end
     end
     end)
     end
     end)

type rsession =
| RDone of Prims.list<Prims.string> * Prims.list<capture>
| RStopped of rfault


let uu___is_RDone : rsession  ->  Prims.bool = (fun ( projectee  :  rsession ) -> (match (projectee) with
| RDone (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RDone__item___0 : rsession  ->  Prims.list<Prims.string> = (fun ( projectee  :  rsession ) -> (match (projectee) with
| RDone (_0, _1) -> begin
     _0
     end))


let __proj__RDone__item___1 : rsession  ->  Prims.list<capture> = (fun ( projectee  :  rsession ) -> (match (projectee) with
| RDone (_0, _1) -> begin
     _1
     end))


let uu___is_RStopped : rsession  ->  Prims.bool = (fun ( projectee  :  rsession ) -> (match (projectee) with
| RStopped (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RStopped__item___0 : rsession  ->  rfault = (fun ( projectee  :  rsession ) -> (match (projectee) with
| RStopped (_0) -> begin
     _0
     end))


let rec replay_session : (Prims.string  ->  Prims.bool)  ->  Prims.list<creq>  ->  Prims.list<capture>  ->  rsession = (fun ( canonical  :  Prims.string  ->  Prims.bool ) ( qs  :  Prims.list<creq> ) ( cs  :  Prims.list<capture> ) -> (match (qs) with
| [] -> begin
     RDone ([], cs)
     end
| (q)::t -> begin
     (match ((replay_strict canonical q cs)) with
| RFault (f) -> begin
     RStopped (f)
     end
| RValue (v, rest) -> begin
     (match ((replay_session canonical t rest)) with
| RDone (vs, r) -> begin
     RDone ((v)::vs, r)
     end
| RStopped (f) -> begin
     RStopped (f)
     end)
     end)
     end))


let rec values_of : Prims.list<creq>  ->  Prims.list<Prims.string> = (fun ( qs  :  Prims.list<creq> ) -> (match (qs) with
| [] -> begin
     []
     end
| (q)::t -> begin
     (q.qval)::(values_of t)
     end))


let rec labels_ok : (Prims.string  ->  Prims.bool)  ->  Prims.list<creq>  ->  Prims.bool = (fun ( canonical  :  Prims.string  ->  Prims.bool ) ( qs  :  Prims.list<creq> ) -> (match (qs) with
| [] -> begin
     true
     end
| (q)::t -> begin
     ((canonical q.qdet) && (labels_ok canonical t))
     end))


let rec replace_value : Prims.list<capture>  ->  pos  ->  Prims.string  ->  Prims.list<capture> = (fun ( cs  :  Prims.list<capture> ) ( n  :  pos ) ( v'  :  Prims.string ) -> (match (((cs), (n))) with
| ([], uu___) -> begin
     []
     end
| ((c)::t, PZero) -> begin
     ({pseq = c.pseq; peff = c.peff; pdet = c.pdet; pval = v'; pprev = c.pprev; phash = c.phash})::t
     end
| ((c)::t, PSucc (m)) -> begin
     (c)::(replace_value t m v')
     end))


let rec capture_value_at : Prims.list<capture>  ->  pos  ->  found<Prims.string> = (fun ( cs  :  Prims.list<capture> ) ( n  :  pos ) -> (match (((cs), (n))) with
| ([], uu___) -> begin
     Missing
     end
| ((c)::uu___, PZero) -> begin
     Found (c.pval)
     end
| ((uu___)::t, PSucc (m)) -> begin
     (capture_value_at t m)
     end))


let rec chain_head = (fun ( rs  :  Prims.list<record<'op>> ) -> (match (rs) with
| [] -> begin
     ""
     end
| (r)::[] -> begin
     r.rhash
     end
| (uu___)::t -> begin
     (chain_head t)
     end))

type attestation = {ahead : Prims.string; akey : Prims.string; asig : Prims.string}


let __proj__Mkattestation__item__ahead : attestation  ->  Prims.string = (fun ( projectee  :  attestation ) -> (match (projectee) with
| {ahead = ahead; akey = akey; asig = asig} -> begin
     ahead
     end))


let __proj__Mkattestation__item__akey : attestation  ->  Prims.string = (fun ( projectee  :  attestation ) -> (match (projectee) with
| {ahead = ahead; akey = akey; asig = asig} -> begin
     akey
     end))


let __proj__Mkattestation__item__asig : attestation  ->  Prims.string = (fun ( projectee  :  attestation ) -> (match (projectee) with
| {ahead = ahead; akey = akey; asig = asig} -> begin
     asig
     end))


let attest_head = (fun ( sign  :  Prims.string  ->  found<attestation> ) ( rs  :  Prims.list<record<'op>> ) -> (sign (chain_head rs)))


let verify_attestation = (fun ( verify  :  attestation  ->  Prims.string  ->  Prims.bool ) ( att  :  attestation ) ( rs  :  Prims.list<record<'op>> ) -> (verify att (chain_head rs)))


let accepts_signed = (fun ( h  :  Prims.string  ->  Prims.string  ->  Prims.string ) ( show  :  pos  ->  Prims.string ) ( enc_op  :  'op  ->  Prims.string ) ( genesis  :  Prims.string ) ( verify  :  attestation  ->  Prims.string  ->  Prims.bool ) ( att  :  attestation ) ( rs  :  Prims.list<record<'op>> ) -> ((verify_chain h show enc_op genesis rs) && (verify_attestation verify att rs)))


let rec step_at = (fun ( cs  :  Prims.list<cstep<'op>> ) ( n  :  pos ) -> (match (((cs), (n))) with
| ([], uu___) -> begin
     Missing
     end
| ((c)::uu___, PZero) -> begin
     Found (c)
     end
| ((uu___)::t, PSucc (m)) -> begin
     (step_at t m)
     end))


let rec replace_step = (fun ( cs  :  Prims.list<cstep<'op>> ) ( n  :  pos ) ( c'  :  cstep<'op> ) -> (match (((cs), (n))) with
| ([], uu___) -> begin
     []
     end
| ((uu___)::t, PZero) -> begin
     (c')::t
     end
| ((c)::t, PSucc (m)) -> begin
     (c)::(replace_step t m c')
     end))


let rec insert_step = (fun ( cs  :  Prims.list<cstep<'op>> ) ( n  :  pos ) ( c'  :  cstep<'op> ) -> (match (((cs), (n))) with
| ([], uu___) -> begin
     (c')::[]
     end
| (uu___, PZero) -> begin
     (c')::cs
     end
| ((c)::t, PSucc (m)) -> begin
     (c)::(insert_step t m c')
     end))


let rec remove_step = (fun ( cs  :  Prims.list<cstep<'op>> ) ( n  :  pos ) -> (match (((cs), (n))) with
| ([], uu___) -> begin
     []
     end
| ((uu___)::t, PZero) -> begin
     t
     end
| ((c)::t, PSucc (m)) -> begin
     (c)::(remove_step t m)
     end))

type splice<'op> =
| Replaced of pos * cstep<'op>
| Inserted of pos * cstep<'op>
| Dropped of pos


let uu___is_Replaced = (fun ( projectee  :  splice<'op> ) -> (match (projectee) with
| Replaced (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Replaced__item___0 = (fun ( projectee  :  splice<'op> ) -> (match (projectee) with
| Replaced (_0, _1) -> begin
     _0
     end))


let __proj__Replaced__item___1 = (fun ( projectee  :  splice<'op> ) -> (match (projectee) with
| Replaced (_0, _1) -> begin
     _1
     end))


let uu___is_Inserted = (fun ( projectee  :  splice<'op> ) -> (match (projectee) with
| Inserted (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Inserted__item___0 = (fun ( projectee  :  splice<'op> ) -> (match (projectee) with
| Inserted (_0, _1) -> begin
     _0
     end))


let __proj__Inserted__item___1 = (fun ( projectee  :  splice<'op> ) -> (match (projectee) with
| Inserted (_0, _1) -> begin
     _1
     end))


let uu___is_Dropped = (fun ( projectee  :  splice<'op> ) -> (match (projectee) with
| Dropped (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Dropped__item___0 = (fun ( projectee  :  splice<'op> ) -> (match (projectee) with
| Dropped (_0) -> begin
     _0
     end))


let apply_splice = (fun ( cs  :  Prims.list<cstep<'op>> ) ( sp  :  splice<'op> ) -> (match (sp) with
| Replaced (n, c') -> begin
     (replace_step cs n c')
     end
| Inserted (n, c') -> begin
     (insert_step cs n c')
     end
| Dropped (n) -> begin
     (remove_step cs n)
     end))


let splice_changes = (fun ( cs  :  Prims.list<cstep<'op>> ) ( sp  :  splice<'op> ) -> (match (sp) with
| Replaced (n, c') -> begin
     (match ((step_at cs n)) with
| Missing -> begin
     false
     end
| Found (c) -> begin
     (not ((Prims.op_Equals c c')))
     end)
     end
| Inserted (uu___, uu___1) -> begin
     true
     end
| Dropped (n) -> begin
     (match ((step_at cs n)) with
| Missing -> begin
     false
     end
| Found (uu___) -> begin
     true
     end)
     end))

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


let twin_show : pos  ->  Prims.string = (fun ( p  :  pos ) -> (match (p) with
| PZero -> begin
     "0"
     end
| PSucc (uu___) -> begin
     "n"
     end))


let twin_h : Prims.string  ->  Prims.string  ->  Prims.string = (fun ( p  :  Prims.string ) ( e  :  Prims.string ) -> (Prims.strcat p (Prims.strcat "/" e)))


let twin_enc : Prims.string  ->  Prims.string = (fun ( o  :  Prims.string ) -> o)


let twin_le : Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( x  :  Prims.string ) ( y  :  Prims.string ) -> ((Prims.op_Equals x "a") || (Prims.op_Equals y "b")))


let twin_chain : Prims.list<record<Prims.string>> = (build_chain twin_h twin_show twin_enc "" PZero (({cactor = "A"; cop = "x"})::({cactor = "B"; cop = "y"})::[]))


let twin_tampered : Prims.list<record<Prims.string>> = (match ((record_at twin_chain (PSucc (PZero)))) with
| Found (r) -> begin
     (replace_at twin_chain (PSucc (PZero)) {rseq = r.rseq; ractor = r.ractor; rop = "z"; rprev = r.rprev; rhash = r.rhash})
     end
| Missing -> begin
     twin_chain
     end)


let twins : Prims.list<twin> = ({tname = "join-comma-joins"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (join_comma (("a")::("b")::("c")::[])) "a,b,c"))})::({tname = "first-absent-names-the-missing-parent"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (first_absent (("k")::[]) (("k")::("p")::[])) (Found ("p"))))})::({tname = "verify-chain-accepts-an-appended-chain"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (verify_chain twin_h twin_show twin_enc "" twin_chain) true))})::({tname = "verify-chain-refuses-an-op-tamper"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (verify_chain twin_h twin_show twin_enc "" twin_tampered) false))})::({tname = "first-break-finds-nothing-in-an-intact-dag"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (first_break twin_h twin_enc (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) -> true) (({ekey = "/A|x"; enode = {dparents = []; dactor = "A"; dop = "x"}})::[])) Missing))})::({tname = "first-break-names-a-tampered-node"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (first_break twin_h twin_enc (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) -> true) (({ekey = "bad"; enode = {dparents = []; dactor = "A"; dop = "x"}})::[])) (Found ({bnode = "bad"; breason = "content-id mismatch (tampered node)"; bexpected = "/A|x"; bgot = "bad"}))))})::({tname = "merge-all-parents-dedups-and-sorts"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (merge_all_parents twin_le (("b")::("a")::("b")::[])) (("a")::("b")::[])))})::({tname = "merge-all-id-is-set-determined"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (merge_all_id twin_h twin_enc twin_le (("b")::("a")::[]) "A" "x") (merge_all_id twin_h twin_enc twin_le (("a")::("b")::("a")::[]) "A" "x")))})::[]




