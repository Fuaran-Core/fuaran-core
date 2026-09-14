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




