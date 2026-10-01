module DecimalText

let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let rec app = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     m
     end
| (x)::t -> begin
     (x)::(app t m)
     end))


let rec rev = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (app (rev t) ((x)::[]))
     end))


let rec take = (fun ( n  :  Prims.nat ) ( l  :  Prims.list<'a> ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     []
     end else begin
     (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (x)::(take (n - (Prims.parse_int "1")) t)
     end)
     end)


let rec drop = (fun ( n  :  Prims.nat ) ( l  :  Prims.list<'a> ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     l
     end else begin
     (match (l) with
| [] -> begin
     []
     end
| (uu___)::t -> begin
     (drop (n - (Prims.parse_int "1")) t)
     end)
     end)


let max : Prims.nat  ->  Prims.nat  ->  Prims.nat = (fun ( a  :  Prims.nat ) ( b  :  Prims.nat ) ->  
if (a >= b) then begin
     a
     end else begin
     b
     end)


type digit = Prims.nat

type sym =
| Minus
| Dot
| Digit of digit
| Other


let uu___is_Minus : sym  ->  Prims.bool = (fun ( projectee  :  sym ) -> (match (projectee) with
| Minus -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Dot : sym  ->  Prims.bool = (fun ( projectee  :  sym ) -> (match (projectee) with
| Dot -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Digit : sym  ->  Prims.bool = (fun ( projectee  :  sym ) -> (match (projectee) with
| Digit (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Digit__item___0 : sym  ->  digit = (fun ( projectee  :  sym ) -> (match (projectee) with
| Digit (_0) -> begin
     _0
     end))


let uu___is_Other : sym  ->  Prims.bool = (fun ( projectee  :  sym ) -> (match (projectee) with
| Other -> begin
     true
     end
| uu___ -> begin
     false
     end))


let rec zeros : Prims.nat  ->  Prims.list<digit> = (fun ( k  :  Prims.nat ) ->  
if (Prims.op_Equals k (Prims.parse_int "0")) then begin
     []
     end else begin
     ((Prims.parse_int "0"))::(zeros (k - (Prims.parse_int "1")))
     end)


let is_digit : sym  ->  Prims.bool = (fun ( c  :  sym ) -> (match (c) with
| Digit (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let rec all_digits : Prims.list<sym>  ->  Prims.bool = (fun ( s  :  Prims.list<sym> ) -> (match (s) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((is_digit c) && (all_digits t))
     end))


let is_digits : Prims.list<sym>  ->  Prims.bool = (fun ( s  :  Prims.list<sym> ) -> ((match (s) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_digits s)))


let rec to_digits : Prims.list<sym>  ->  Prims.list<digit> = (fun ( s  :  Prims.list<sym> ) -> (match (s) with
| [] -> begin
     []
     end
| (Digit (d))::t -> begin
     (d)::(to_digits t)
     end
| (uu___)::t -> begin
     ((Prims.parse_int "0"))::(to_digits t)
     end))


let rec of_digits : Prims.list<digit>  ->  Prims.list<sym> = (fun ( l  :  Prims.list<digit> ) -> (match (l) with
| [] -> begin
     []
     end
| (d)::t -> begin
     (Digit (d))::(of_digits t)
     end))


let rec split_dot : Prims.list<sym>  ->  FStar_Pervasives_Native.option<(Prims.list<sym> * Prims.list<sym>)> = (fun ( s  :  Prims.list<sym> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if (Prims.op_Equals c Dot) then begin
     FStar_Pervasives_Native.Some ((([]), (t)))
     end else begin
     (match ((split_dot t)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (before, after) -> begin
     FStar_Pervasives_Native.Some ((((c)::before), (after)))
     end)
     end
     end))


let rec trim_start : Prims.list<digit>  ->  Prims.list<digit> = (fun ( l  :  Prims.list<digit> ) -> (match (l) with
| [] -> begin
     []
     end
| (d)::t -> begin
      
if (Prims.op_Equals d (Prims.parse_int "0")) then begin
     (trim_start t)
     end else begin
     l
     end
     end))


let rec trim_end : Prims.list<digit>  ->  Prims.list<digit> = (fun ( l  :  Prims.list<digit> ) -> (match (l) with
| [] -> begin
     []
     end
| (d)::t -> begin
     (

let r = (trim_end t)
in  
if ((Prims.op_Equals d (Prims.parse_int "0")) && (match (r) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     []
     end else begin
     (d)::r
     end)
     end))

type dparts = {negative : Prims.bool; ip : Prims.list<digit>; fp : Prims.list<digit>}


let __proj__Mkdparts__item__negative : dparts  ->  Prims.bool = (fun ( projectee  :  dparts ) -> (match (projectee) with
| {negative = negative; ip = ip; fp = fp} -> begin
     negative
     end))


let __proj__Mkdparts__item__ip : dparts  ->  Prims.list<digit> = (fun ( projectee  :  dparts ) -> (match (projectee) with
| {negative = negative; ip = ip; fp = fp} -> begin
     ip
     end))


let __proj__Mkdparts__item__fp : dparts  ->  Prims.list<digit> = (fun ( projectee  :  dparts ) -> (match (projectee) with
| {negative = negative; ip = ip; fp = fp} -> begin
     fp
     end))


let parts : Prims.list<sym>  ->  FStar_Pervasives_Native.option<dparts> = (fun ( s  :  Prims.list<sym> ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c0)::rest -> begin
     (

let neg = (Prims.op_Equals c0 Minus)
in (

let body =  
if neg then begin
     rest
     end else begin
     s
     end
in (match ((split_dot body)) with
| FStar_Pervasives_Native.None -> begin
      
if (not ((is_digits body))) then begin
     FStar_Pervasives_Native.None
     end else begin
     (

let ip' = (trim_start (to_digits body))
in FStar_Pervasives_Native.Some ({negative = (neg && (match (ip') with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end)); ip = ip'; fp = []}))
     end
     end
| FStar_Pervasives_Native.Some (ip0, fp0) -> begin
      
if (not (((is_digits ip0) && (is_digits fp0)))) then begin
     FStar_Pervasives_Native.None
     end else begin
     (

let ip' = (trim_start (to_digits ip0))
in (

let fp' = (trim_end (to_digits fp0))
in (

let is_zero = ((match (ip') with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) && (match (fp') with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end))
in FStar_Pervasives_Native.Some ({negative = (neg && (not (is_zero))); ip = ip'; fp = fp'}))))
     end
     end)))
     end))


let render : dparts  ->  Prims.list<sym> = (fun ( p  :  dparts ) -> (app ( 
if p.negative then begin
     (Minus)::[]
     end else begin
     []
     end) (app (of_digits ( 
if (match (p.ip) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     ((Prims.parse_int "0"))::[]
     end else begin
     p.ip
     end)) ( 
if (match (p.fp) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     []
     end else begin
     (Dot)::(of_digits p.fp)
     end))))


let try_canonical : Prims.list<sym>  ->  FStar_Pervasives_Native.option<Prims.list<sym>> = (fun ( s  :  Prims.list<sym> ) -> (match ((parts s)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (p) -> begin
     FStar_Pervasives_Native.Some ((render p))
     end))


let is_canonical : Prims.list<sym>  ->  Prims.bool = (fun ( s  :  Prims.list<sym> ) -> (Prims.op_Equals (try_canonical s) (FStar_Pervasives_Native.Some (s))))


let zero_text : Prims.list<sym> = (Digit ((Prims.parse_int "0")))::[]


let pad_right : Prims.list<digit>  ->  Prims.nat  ->  Prims.list<digit> = (fun ( l  :  Prims.list<digit> ) ( n  :  Prims.nat ) ->  
if ((len l) >= n) then begin
     l
     end else begin
     (app l (zeros (n - (len l))))
     end)


let pad_left : Prims.list<digit>  ->  Prims.nat  ->  Prims.list<digit> = (fun ( l  :  Prims.list<digit> ) ( n  :  Prims.nat ) ->  
if ((len l) >= n) then begin
     l
     end else begin
     (app (zeros (n - (len l))) l)
     end)


let aligned : dparts  ->  dparts  ->  (Prims.list<digit> * Prims.list<digit> * Prims.nat) = (fun ( a  :  dparts ) ( b  :  dparts ) -> (

let scale = (max (len a.fp) (len b.fp))
in (

let ma = (app a.ip (pad_right a.fp scale))
in (

let mb = (app b.ip (pad_right b.fp scale))
in (

let width = (max (len ma) (len mb))
in (((pad_left ma width)), ((pad_left mb width)), (scale)))))))


let add_digit : digit  ->  digit  ->  Prims.nat  ->  (digit * Prims.nat) = (fun ( x  :  digit ) ( y  :  digit ) ( c  :  Prims.nat ) -> (

let d = ((x + y) + c)
in  
if (d < (Prims.parse_int "10")) then begin
     ((d), ((Prims.parse_int "0")))
     end else begin
     (((d - (Prims.parse_int "10"))), ((Prims.parse_int "1")))
     end))


let rec add_lsb : Prims.list<digit>  ->  Prims.list<digit>  ->  Prims.nat  ->  Prims.list<digit> = (fun ( a  :  Prims.list<digit> ) ( b  :  Prims.list<digit> ) ( c  :  Prims.nat ) -> (match (((a), (b))) with
| ((x)::xs, (y)::ys) -> begin
     (

let uu___ = (add_digit x y c)
in (match (uu___) with
| (d, c') -> begin
     (d)::(add_lsb xs ys c')
     end))
     end
| (uu___, uu___1) -> begin
     (c)::[]
     end))


let sub_digit : digit  ->  digit  ->  Prims.nat  ->  (digit * Prims.nat) = (fun ( x  :  digit ) ( y  :  digit ) ( br  :  Prims.nat ) -> (

let d = ((x - y) - br)
in  
if (d < (Prims.parse_int "0")) then begin
     (((d + (Prims.parse_int "10"))), ((Prims.parse_int "1")))
     end else begin
     ((d), ((Prims.parse_int "0")))
     end))


let rec sub_lsb : Prims.list<digit>  ->  Prims.list<digit>  ->  Prims.nat  ->  (Prims.list<digit> * Prims.nat) = (fun ( a  :  Prims.list<digit> ) ( b  :  Prims.list<digit> ) ( br  :  Prims.nat ) -> (match (((a), (b))) with
| ((x)::xs, (y)::ys) -> begin
     (

let uu___ = (sub_digit x y br)
in (match (uu___) with
| (d, br') -> begin
     (

let uu___1 = (sub_lsb xs ys br')
in (match (uu___1) with
| (rest, out) -> begin
     (((d)::rest), (out))
     end))
     end))
     end
| (uu___, uu___1) -> begin
     (([]), (br))
     end))


let add_magnitudes : Prims.list<digit>  ->  Prims.list<digit>  ->  Prims.list<digit> = (fun ( a  :  Prims.list<digit> ) ( b  :  Prims.list<digit> ) -> (rev (add_lsb (rev a) (rev b) (Prims.parse_int "0"))))


let sub_magnitudes : Prims.list<digit>  ->  Prims.list<digit>  ->  Prims.list<digit> = (fun ( a  :  Prims.list<digit> ) ( b  :  Prims.list<digit> ) -> (

let uu___ = (sub_lsb (rev a) (rev b) (Prims.parse_int "0"))
in (match (uu___) with
| (digits, uu___1) -> begin
     (rev digits)
     end)))


let rec lex : Prims.list<digit>  ->  Prims.list<digit>  ->  Prims.int = (fun ( a  :  Prims.list<digit> ) ( b  :  Prims.list<digit> ) -> (match (((a), (b))) with
| ([], []) -> begin
     (Prims.parse_int "0")
     end
| ([], uu___) -> begin
     (Prims.parse_int "-1")
     end
| (uu___, []) -> begin
     (Prims.parse_int "1")
     end
| ((x)::xs, (y)::ys) -> begin
      
if (x < y) then begin
     (Prims.parse_int "-1")
     end else begin
      
if (x > y) then begin
     (Prims.parse_int "1")
     end else begin
     (lex xs ys)
     end
     end
     end))


let compare_parts : dparts  ->  dparts  ->  Prims.int = (fun ( pa  :  dparts ) ( pb  :  dparts ) ->  
if (Prims.op_Less_Greater pa.negative pb.negative) then begin
      
if pa.negative then begin
     (Prims.parse_int "-1")
     end else begin
     (Prims.parse_int "1")
     end
     end else begin
     (

let uu___ = (aligned pa pb)
in (match (uu___) with
| (ma, mb, uu___1) -> begin
     (

let magnitude = (lex ma mb)
in  
if pa.negative then begin
     ((Prims.parse_int "0") - magnitude)
     end else begin
     magnitude
     end)
     end))
     end)


let compare : Prims.list<sym>  ->  Prims.list<sym>  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( a  :  Prims.list<sym> ) ( b  :  Prims.list<sym> ) -> (match ((((parts a)), ((parts b)))) with
| (FStar_Pervasives_Native.Some (pa), FStar_Pervasives_Native.Some (pb)) -> begin
     FStar_Pervasives_Native.Some ((compare_parts pa pb))
     end
| (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
     end))


let add_parts : dparts  ->  dparts  ->  dparts = (fun ( pa  :  dparts ) ( pb  :  dparts ) -> (

let uu___ = (aligned pa pb)
in (match (uu___) with
| (ma, mb, scale) -> begin
     (

let uu___1 =  
if (Prims.op_Equals pa.negative pb.negative) then begin
     ((pa.negative), ((add_magnitudes ma mb)))
     end else begin
     (

let c = (lex ma mb)
in  
if (Prims.op_Equals c (Prims.parse_int "0")) then begin
     ((false), ([]))
     end else begin
      
if (c > (Prims.parse_int "0")) then begin
     ((pa.negative), ((sub_magnitudes ma mb)))
     end else begin
     ((pb.negative), ((sub_magnitudes mb ma)))
     end
     end)
     end
in (match (uu___1) with
| (negative, magnitude) -> begin
     (

let magnitude1 = (pad_left magnitude (scale + (Prims.parse_int "1")))
in (

let ip = (take ((len magnitude1) - scale) magnitude1)
in (

let fp = (drop ((len magnitude1) - scale) magnitude1)
in (

let ip' = (trim_start ip)
in (

let fp' = (trim_end fp)
in (

let is_zero = ((match (ip') with
| [] -> begin
     true
     end
| uu___2 -> begin
     false
     end) && (match (fp') with
| [] -> begin
     true
     end
| uu___2 -> begin
     false
     end))
in {negative = (negative && (not (is_zero))); ip = ip'; fp = fp'}))))))
     end))
     end)))


let add : Prims.list<sym>  ->  Prims.list<sym>  ->  FStar_Pervasives_Native.option<Prims.list<sym>> = (fun ( a  :  Prims.list<sym> ) ( b  :  Prims.list<sym> ) -> (match ((((parts a)), ((parts b)))) with
| (FStar_Pervasives_Native.Some (pa), FStar_Pervasives_Native.Some (pb)) -> begin
     FStar_Pervasives_Native.Some ((render (add_parts pa pb)))
     end
| (uu___, uu___1) -> begin
     FStar_Pervasives_Native.None
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


let twins : Prims.list<twin> = ({tname = "try-canonical-trims-a-negative"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (try_canonical ((Minus)::(Digit ((Prims.parse_int "0")))::(Digit ((Prims.parse_int "0")))::(Dot)::(Digit ((Prims.parse_int "5")))::(Digit ((Prims.parse_int "0")))::[])) (FStar_Pervasives_Native.Some ((Minus)::(Digit ((Prims.parse_int "0")))::(Dot)::(Digit ((Prims.parse_int "5")))::[]))))})::({tname = "add-carries-across-the-point"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (add ((Digit ((Prims.parse_int "1")))::(Dot)::(Digit ((Prims.parse_int "5")))::[]) ((Digit ((Prims.parse_int "2")))::(Dot)::(Digit ((Prims.parse_int "5")))::[])) (FStar_Pervasives_Native.Some ((Digit ((Prims.parse_int "4")))::[]))))})::({tname = "compare-orders-numerically"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (compare ((Digit ((Prims.parse_int "1")))::(Dot)::(Digit ((Prims.parse_int "5")))::[]) ((Digit ((Prims.parse_int "2")))::[])) (FStar_Pervasives_Native.Some ((Prims.parse_int "-1")))))})::[]




