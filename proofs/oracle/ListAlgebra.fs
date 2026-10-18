module ListAlgebra

let rec app = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     m
     end
| (x)::t -> begin
     (x)::(app t m)
     end))


let rec mem = (fun ( x  :  'a ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals x y) || (mem x t))
     end))


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let rec rev = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (app (rev t) ((x)::[]))
     end))


let rec distinct = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((not ((mem x t))) && (distinct t))
     end))


let rec inter = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (match (x) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (mem h y) then begin
     (h)::(inter t y)
     end else begin
     (inter t y)
     end
     end))


let rec diff = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (match (x) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (mem h y) then begin
     (diff t y)
     end else begin
     (h)::(diff t y)
     end
     end))


let twins : Prims.list<Twin.twin> = ({Twin.tname = "app-appends"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (app (("a")::("b")::[]) (("c")::[])) (("a")::("b")::("c")::[])) && (Prims.op_Equals (app [] (("c")::[])) (("c")::[]))))})::({Twin.tname = "mem-finds-a-member-and-only-a-member"; Twin.tholds = (fun ( uu___  :  unit ) -> (((mem "b" (("a")::("b")::[])) && (not ((mem "c" (("a")::("b")::[]))))) && (not ((mem "a" [])))))})::({Twin.tname = "len-counts"; Twin.tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (len (("a")::("b")::("c")::[])) (Prims.parse_int "3")) && (Prims.op_Equals (len []) (Prims.parse_int "0"))))})::({Twin.tname = "rev-reverses"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (rev (("a")::("b")::("c")::[])) (("c")::("b")::("a")::[])))})::({Twin.tname = "distinct-refuses-a-repeat"; Twin.tholds = (fun ( uu___  :  unit ) -> ((distinct (("a")::("b")::("c")::[])) && (not ((distinct (("a")::("b")::("a")::[]))))))})::({Twin.tname = "inter-keeps-the-left-order"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (inter (("a")::("b")::("c")::[]) (("c")::("a")::[])) (("a")::("c")::[])))})::({Twin.tname = "diff-keeps-the-left-order"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (diff (("a")::("b")::("c")::[]) (("b")::[])) (("a")::("c")::[])))})::[]




