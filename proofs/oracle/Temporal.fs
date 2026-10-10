module Temporal
type time_unit =
| Seconds
| Milliseconds
| Microseconds
| Nanoseconds


let uu___is_Seconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Seconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Milliseconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Milliseconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Microseconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Microseconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Nanoseconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Nanoseconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let unit_digits : time_unit  ->  Prims.nat = (fun ( u  :  time_unit ) -> (match (u) with
| Seconds -> begin
     (Prims.parse_int "0")
     end
| Milliseconds -> begin
     (Prims.parse_int "3")
     end
| Microseconds -> begin
     (Prims.parse_int "6")
     end
| Nanoseconds -> begin
     (Prims.parse_int "9")
     end))


let unit_scale : time_unit  ->  Prims.pos = (fun ( u  :  time_unit ) -> (match (u) with
| Seconds -> begin
     (Prims.parse_int "1")
     end
| Milliseconds -> begin
     (Prims.parse_int "1000")
     end
| Microseconds -> begin
     (Prims.parse_int "1000000")
     end
| Nanoseconds -> begin
     (Prims.parse_int "1000000000")
     end))


let of_digits : Prims.int  ->  time_unit = (fun ( d  :  Prims.int ) ->  
if (d <= (Prims.parse_int "0")) then begin
     Seconds
     end else begin
      
if (d <= (Prims.parse_int "3")) then begin
     Milliseconds
     end else begin
      
if (d <= (Prims.parse_int "6")) then begin
     Microseconds
     end else begin
     Nanoseconds
     end
     end
     end)


let unit_widens : time_unit  ->  time_unit  ->  Prims.bool = (fun ( from  :  time_unit ) ( target  :  time_unit ) -> ((unit_digits from) <= (unit_digits target)))


let rec pow10 : Prims.nat  ->  Prims.pos = (fun ( k  :  Prims.nat ) ->  
if (Prims.op_Equals k (Prims.parse_int "0")) then begin
     (Prims.parse_int "1")
     end else begin
     ((Prims.parse_int "10") * (pow10 (k - (Prims.parse_int "1"))))
     end)


let is_leap : Prims.int  ->  Prims.bool = (fun ( y  :  Prims.int ) -> ((Prims.op_Equals (Prims.mod_f y (Prims.parse_int "4")) (Prims.parse_int "0")) && ((Prims.op_Less_Greater (Prims.mod_f y (Prims.parse_int "100")) (Prims.parse_int "0")) || (Prims.op_Equals (Prims.mod_f y (Prims.parse_int "400")) (Prims.parse_int "0")))))


let days_in : Prims.int  ->  Prims.int  ->  Prims.int = (fun ( y  :  Prims.int ) ( m  :  Prims.int ) ->  
if (Prims.op_Equals m (Prims.parse_int "2")) then begin
      
if (is_leap y) then begin
     (Prims.parse_int "29")
     end else begin
     (Prims.parse_int "28")
     end
     end else begin
      
if ((((Prims.op_Equals m (Prims.parse_int "4")) || (Prims.op_Equals m (Prims.parse_int "6"))) || (Prims.op_Equals m (Prims.parse_int "9"))) || (Prims.op_Equals m (Prims.parse_int "11"))) then begin
     (Prims.parse_int "30")
     end else begin
     (Prims.parse_int "31")
     end
     end)


let days_of_civil : Prims.int  ->  Prims.int  ->  Prims.int  ->  Prims.int = (fun ( y  :  Prims.int ) ( m  :  Prims.int ) ( d  :  Prims.int ) -> (

let y1 =  
if (m <= (Prims.parse_int "2")) then begin
     (y - (Prims.parse_int "1"))
     end else begin
     y
     end
in (

let y4 = (y1 + (Prims.parse_int "400"))
in (

let era = (y4 / (Prims.parse_int "400"))
in (

let yoe = (y4 - (era * (Prims.parse_int "400")))
in (

let mp = (Prims.mod_f (m + (Prims.parse_int "9")) (Prims.parse_int "12"))
in (

let doy = ((((((Prims.parse_int "153") * mp) + (Prims.parse_int "2")) / (Prims.parse_int "5")) + d) - (Prims.parse_int "1"))
in (

let doe = ((((yoe * (Prims.parse_int "365")) + (yoe / (Prims.parse_int "4"))) - (yoe / (Prims.parse_int "100"))) + doy)
in ((((era - (Prims.parse_int "1")) * (Prims.parse_int "146097")) + doe) - (Prims.parse_int "719468"))))))))))

type civil = {year : Prims.int; month : Prims.int; day : Prims.int}


let __proj__Mkcivil__item__year : civil  ->  Prims.int = (fun ( projectee  :  civil ) -> (match (projectee) with
| {year = year; month = month; day = day} -> begin
     year
     end))


let __proj__Mkcivil__item__month : civil  ->  Prims.int = (fun ( projectee  :  civil ) -> (match (projectee) with
| {year = year; month = month; day = day} -> begin
     month
     end))


let __proj__Mkcivil__item__day : civil  ->  Prims.int = (fun ( projectee  :  civil ) -> (match (projectee) with
| {year = year; month = month; day = day} -> begin
     day
     end))


let civil_of_days : Prims.int  ->  civil = (fun ( days  :  Prims.int ) -> (

let z = ((days + (Prims.parse_int "719468")) + (Prims.parse_int "146097"))
in (

let era = (z / (Prims.parse_int "146097"))
in (

let doe = (z - (era * (Prims.parse_int "146097")))
in (

let yoe = ((((doe - (doe / (Prims.parse_int "1460"))) + (doe / (Prims.parse_int "36524"))) - (doe / (Prims.parse_int "146096"))) / (Prims.parse_int "365"))
in (

let doy = (doe - ((((Prims.parse_int "365") * yoe) + (yoe / (Prims.parse_int "4"))) - (yoe / (Prims.parse_int "100"))))
in (

let mp = ((((Prims.parse_int "5") * doy) + (Prims.parse_int "2")) / (Prims.parse_int "153"))
in (

let d = ((doy - ((((Prims.parse_int "153") * mp) + (Prims.parse_int "2")) / (Prims.parse_int "5"))) + (Prims.parse_int "1"))
in (

let m =  
if (mp < (Prims.parse_int "10")) then begin
     (mp + (Prims.parse_int "3"))
     end else begin
     (mp - (Prims.parse_int "9"))
     end
in {year = ((yoe + ((era - (Prims.parse_int "1")) * (Prims.parse_int "400"))) +  
if (m <= (Prims.parse_int "2")) then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "0")
     end); month = m; day = d})))))))))


let min_day : Prims.int = (Prims.parse_int "-719528")


let max_day : Prims.int = (Prims.parse_int "2932896")


let is_day_in_range : Prims.int  ->  Prims.bool = (fun ( days  :  Prims.int ) -> ((min_day <= days) && (days <= max_day)))


let civil_valid : civil  ->  Prims.bool = (fun ( c  :  civil ) -> (((((Prims.parse_int "1") <= c.month) && (c.month <= (Prims.parse_int "12"))) && ((Prims.parse_int "1") <= c.day)) && (c.day <= (days_in c.year c.month))))


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let dec_ch : Prims.int  ->  WireCanon.ch = (fun ( n  :  Prims.int ) -> (match (n) with
| uu___ when (uu___ = (Prims.parse_int "0")) -> begin
     WireCanon.CHexCh (WireCanon.HD0)
     end
| uu___ when (uu___ = (Prims.parse_int "1")) -> begin
     WireCanon.CHexCh (WireCanon.HD1)
     end
| uu___ when (uu___ = (Prims.parse_int "2")) -> begin
     WireCanon.CHexCh (WireCanon.HD2)
     end
| uu___ when (uu___ = (Prims.parse_int "3")) -> begin
     WireCanon.CHexCh (WireCanon.HD3)
     end
| uu___ when (uu___ = (Prims.parse_int "4")) -> begin
     WireCanon.CHexCh (WireCanon.HD4)
     end
| uu___ when (uu___ = (Prims.parse_int "5")) -> begin
     WireCanon.CHexCh (WireCanon.HD5)
     end
| uu___ when (uu___ = (Prims.parse_int "6")) -> begin
     WireCanon.CHexCh (WireCanon.HD6)
     end
| uu___ when (uu___ = (Prims.parse_int "7")) -> begin
     WireCanon.CHexCh (WireCanon.HD7)
     end
| uu___ when (uu___ = (Prims.parse_int "8")) -> begin
     WireCanon.CHexCh (WireCanon.HD8)
     end
| uu___ -> begin
     WireCanon.CHexCh (WireCanon.HD9)
     end))


let dec_val : WireCanon.ch  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( c  :  WireCanon.ch ) -> (match (c) with
| WireCanon.CHexCh (WireCanon.HD0) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "0"))
     end
| WireCanon.CHexCh (WireCanon.HD1) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "1"))
     end
| WireCanon.CHexCh (WireCanon.HD2) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "2"))
     end
| WireCanon.CHexCh (WireCanon.HD3) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "3"))
     end
| WireCanon.CHexCh (WireCanon.HD4) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "4"))
     end
| WireCanon.CHexCh (WireCanon.HD5) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "5"))
     end
| WireCanon.CHexCh (WireCanon.HD6) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "6"))
     end
| WireCanon.CHexCh (WireCanon.HD7) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "7"))
     end
| WireCanon.CHexCh (WireCanon.HD8) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "8"))
     end
| WireCanon.CHexCh (WireCanon.HD9) -> begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "9"))
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let rec nat_text : Prims.nat  ->  Prims.list<WireCanon.ch> = (fun ( n  :  Prims.nat ) ->  
if (n < (Prims.parse_int "10")) then begin
     ((dec_ch n))::[]
     end else begin
     (WireCanon.app (nat_text (n / (Prims.parse_int "10"))) (((dec_ch (Prims.mod_f n (Prims.parse_int "10"))))::[]))
     end)


let int_text : Prims.int  ->  Prims.list<WireCanon.ch> = (fun ( n  :  Prims.int ) ->  
if (n < (Prims.parse_int "0")) then begin
     (WireCanon.CMinus)::(nat_text ((Prims.parse_int "0") - n))
     end else begin
     (nat_text n)
     end)


let rec zeros : Prims.nat  ->  Prims.list<WireCanon.ch> = (fun ( k  :  Prims.nat ) ->  
if (Prims.op_Equals k (Prims.parse_int "0")) then begin
     []
     end else begin
     (WireCanon.CHexCh (WireCanon.HD0))::(zeros (k - (Prims.parse_int "1")))
     end)


let pad : Prims.nat  ->  Prims.int  ->  Prims.list<WireCanon.ch> = (fun ( width  :  Prims.nat ) ( n  :  Prims.int ) -> (

let s = (int_text n)
in  
if ((len s) >= width) then begin
     s
     end else begin
     (WireCanon.app (zeros (width - (len s))) s)
     end))


let rec read_digits : Prims.list<WireCanon.ch>  ->  Prims.nat  ->  Prims.int  ->  FStar_Pervasives_Native.option<(Prims.int * Prims.list<WireCanon.ch>)> = (fun ( s  :  Prims.list<WireCanon.ch> ) ( count  :  Prims.nat ) ( acc  :  Prims.int ) ->  
if (Prims.op_Equals count (Prims.parse_int "0")) then begin
     FStar_Pervasives_Native.Some (((acc), (s)))
     end else begin
     (match (s) with
| (c)::t -> begin
     (match ((dec_val c)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (read_digits t (count - (Prims.parse_int "1")) ((acc * (Prims.parse_int "10")) + v))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| [] -> begin
     FStar_Pervasives_Native.None
     end)
     end)


let date_part : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<(Prims.int * Prims.int * Prims.int * Prims.list<WireCanon.ch>)> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match ((read_digits s (Prims.parse_int "4") (Prims.parse_int "0"))) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (y, r1) -> begin
     (match (r1) with
| (WireCanon.CMinus)::r2 -> begin
     (match ((read_digits r2 (Prims.parse_int "2") (Prims.parse_int "0"))) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (m, r3) -> begin
     (match (r3) with
| (WireCanon.CMinus)::r4 -> begin
     (match ((read_digits r4 (Prims.parse_int "2") (Prims.parse_int "0"))) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (d, r5) -> begin
      
if (((((Prims.parse_int "1") <= m) && (m <= (Prims.parse_int "12"))) && ((Prims.parse_int "1") <= d)) && (d <= (days_in y m))) then begin
     FStar_Pervasives_Native.Some (((y), (m), (d), (r5)))
     end else begin
     FStar_Pervasives_Native.None
     end
     end)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end)
     end))


let try_days : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match ((date_part s)) with
| FStar_Pervasives_Native.Some (y, m, d, []) -> begin
     FStar_Pervasives_Native.Some ((days_of_civil y m d))
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let is_canonical_date : Prims.list<WireCanon.ch>  ->  Prims.bool = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match ((try_days s)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let civil_text : civil  ->  Prims.list<WireCanon.ch> = (fun ( c  :  civil ) -> (WireCanon.app (pad (Prims.parse_int "4") c.year) ((WireCanon.CMinus)::(WireCanon.app (pad (Prims.parse_int "2") c.month) ((WireCanon.CMinus)::(pad (Prims.parse_int "2") c.day))))))


let date_text : Prims.int  ->  Prims.list<WireCanon.ch> = (fun ( days  :  Prims.int ) -> (civil_text (civil_of_days days)))


let ch_T : WireCanon.ch = WireCanon.CPlain ("T")


let ch_Z : WireCanon.ch = WireCanon.CPlain ("Z")


let rec read_fraction : Prims.list<WireCanon.ch>  ->  Prims.nat  ->  Prims.int  ->  Prims.int  ->  FStar_Pervasives_Native.option<(Prims.nat * Prims.int)> = (fun ( s  :  Prims.list<WireCanon.ch> ) ( n  :  Prims.nat ) ( acc  :  Prims.int ) ( last  :  Prims.int ) -> (match (s) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::[] -> begin
      
if ((((Prims.op_Equals c ch_Z) && ((Prims.parse_int "1") <= n)) && (n <= (Prims.parse_int "9"))) && (Prims.op_Less_Greater last (Prims.parse_int "0"))) then begin
     FStar_Pervasives_Native.Some (((n), (acc)))
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (c)::t -> begin
     (match ((dec_val c)) with
| FStar_Pervasives_Native.Some (v) -> begin
     (read_fraction t (n + (Prims.parse_int "1")) ((acc * (Prims.parse_int "10")) + v) v)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end))


let fraction_part : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<(Prims.nat * Prims.int)> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| (c)::[] -> begin
      
if (Prims.op_Equals c ch_Z) then begin
     FStar_Pervasives_Native.Some ((((Prims.parse_int "0")), ((Prims.parse_int "0"))))
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| (WireCanon.CDot)::t -> begin
     (read_fraction t (Prims.parse_int "0") (Prims.parse_int "0") (Prims.parse_int "0"))
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let read_clock : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<(Prims.int * Prims.nat * Prims.int)> = (fun ( r0  :  Prims.list<WireCanon.ch> ) -> (match (r0) with
| (c)::r1 -> begin
      
if (Prims.op_Less_Greater c ch_T) then begin
     FStar_Pervasives_Native.None
     end else begin
     (match ((read_digits r1 (Prims.parse_int "2") (Prims.parse_int "0"))) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (h, r2) -> begin
     (match (r2) with
| (WireCanon.CColon)::r3 -> begin
     (match ((read_digits r3 (Prims.parse_int "2") (Prims.parse_int "0"))) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (mi, r4) -> begin
     (match (r4) with
| (WireCanon.CColon)::r5 -> begin
     (match ((read_digits r5 (Prims.parse_int "2") (Prims.parse_int "0"))) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (se, r6) -> begin
     (match ((fraction_part r6)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (n, f) -> begin
      
if (((h <= (Prims.parse_int "23")) && (mi <= (Prims.parse_int "59"))) && (se <= (Prims.parse_int "59"))) then begin
     FStar_Pervasives_Native.Some ((((((h * (Prims.parse_int "3600")) + (mi * (Prims.parse_int "60"))) + se)), (n), (f)))
     end else begin
     FStar_Pervasives_Native.None
     end
     end)
     end)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end
     end
| [] -> begin
     FStar_Pervasives_Native.None
     end))


let parse_instant : Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<(Prims.int * Prims.nat * Prims.int)> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match ((date_part s)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (y, m, d, r0) -> begin
     (match ((read_clock r0)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (sod, n, f) -> begin
     FStar_Pervasives_Native.Some ((((((days_of_civil y m d) * (Prims.parse_int "86400")) + sod)), (n), (f)))
     end)
     end))


let is_canonical_timestamp : Prims.list<WireCanon.ch>  ->  Prims.bool = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match ((parse_instant s)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let unit_of : Prims.list<WireCanon.ch>  ->  time_unit = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match ((parse_instant s)) with
| FStar_Pervasives_Native.Some (uu___, n, uu___1) -> begin
     (of_digits n)
     end
| FStar_Pervasives_Native.None -> begin
     Seconds
     end))


let try_instant : time_unit  ->  Prims.list<WireCanon.ch>  ->  FStar_Pervasives_Native.option<(Prims.int * Prims.int)> = (fun ( u  :  time_unit ) ( s  :  Prims.list<WireCanon.ch> ) -> (match ((parse_instant s)) with
| FStar_Pervasives_Native.Some (second, n, f) -> begin
      
if (n <= (unit_digits u)) then begin
     FStar_Pervasives_Native.Some (((second), ((f * (pow10 ((unit_digits u) - n))))))
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end))


let min_second : Prims.int = (min_day * (Prims.parse_int "86400"))


let max_second : Prims.int = ((max_day * (Prims.parse_int "86400")) + (Prims.parse_int "86399"))


let is_instant_in_range : time_unit  ->  Prims.int  ->  Prims.int  ->  Prims.bool = (fun ( u  :  time_unit ) ( second  :  Prims.int ) ( fraction  :  Prims.int ) -> ((((min_second <= second) && (second <= max_second)) && ((Prims.parse_int "0") <= fraction)) && (fraction < (unit_scale u))))


let rec trim_zeros : Prims.list<WireCanon.ch>  ->  Prims.list<WireCanon.ch> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (

let r = (trim_zeros t)
in  
if ((Prims.op_Equals c (WireCanon.CHexCh (WireCanon.HD0))) && (match (r) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     []
     end else begin
     (c)::r
     end)
     end))


let fraction_text : time_unit  ->  Prims.int  ->  Prims.list<WireCanon.ch> = (fun ( u  :  time_unit ) ( fraction  :  Prims.int ) ->  
if (Prims.op_Equals fraction (Prims.parse_int "0")) then begin
     []
     end else begin
     (WireCanon.CDot)::(trim_zeros (pad (unit_digits u) fraction))
     end)


let clock_text : time_unit  ->  Prims.int  ->  Prims.int  ->  Prims.list<WireCanon.ch> = (fun ( u  :  time_unit ) ( sod  :  Prims.int ) ( fraction  :  Prims.int ) -> (ch_T)::(WireCanon.app (pad (Prims.parse_int "2") (sod / (Prims.parse_int "3600"))) ((WireCanon.CColon)::(WireCanon.app (pad (Prims.parse_int "2") ((Prims.mod_f sod (Prims.parse_int "3600")) / (Prims.parse_int "60"))) ((WireCanon.CColon)::(WireCanon.app (pad (Prims.parse_int "2") (Prims.mod_f sod (Prims.parse_int "60"))) (WireCanon.app (fraction_text u fraction) ((ch_Z)::[]))))))))


let instant_text : time_unit  ->  Prims.int  ->  Prims.int  ->  Prims.list<WireCanon.ch> = (fun ( u  :  time_unit ) ( second  :  Prims.int ) ( fraction  :  Prims.int ) -> (

let day = (((second - min_second) / (Prims.parse_int "86400")) + min_day)
in (

let sod = (second - (day * (Prims.parse_int "86400")))
in (WireCanon.app (date_text day) (clock_text u sod fraction)))))


let hexd_rank : WireCanon.hexd  ->  Prims.int = (fun ( d  :  WireCanon.hexd ) -> (match (d) with
| WireCanon.HD0 -> begin
     (Prims.parse_int "48")
     end
| WireCanon.HD1 -> begin
     (Prims.parse_int "49")
     end
| WireCanon.HD2 -> begin
     (Prims.parse_int "50")
     end
| WireCanon.HD3 -> begin
     (Prims.parse_int "51")
     end
| WireCanon.HD4 -> begin
     (Prims.parse_int "52")
     end
| WireCanon.HD5 -> begin
     (Prims.parse_int "53")
     end
| WireCanon.HD6 -> begin
     (Prims.parse_int "54")
     end
| WireCanon.HD7 -> begin
     (Prims.parse_int "55")
     end
| WireCanon.HD8 -> begin
     (Prims.parse_int "56")
     end
| WireCanon.HD9 -> begin
     (Prims.parse_int "57")
     end
| WireCanon.HDa -> begin
     (Prims.parse_int "97")
     end
| WireCanon.HDb -> begin
     (Prims.parse_int "98")
     end
| WireCanon.HDc -> begin
     (Prims.parse_int "99")
     end
| WireCanon.HDd -> begin
     (Prims.parse_int "100")
     end
| WireCanon.HDe -> begin
     (Prims.parse_int "101")
     end
| WireCanon.HDf -> begin
     (Prims.parse_int "102")
     end))


let hexd_val : WireCanon.hexd  ->  Prims.int = (fun ( d  :  WireCanon.hexd ) ->  
if ((hexd_rank d) < (Prims.parse_int "97")) then begin
     ((hexd_rank d) - (Prims.parse_int "48"))
     end else begin
     ((hexd_rank d) - (Prims.parse_int "87"))
     end)


let ch_rank : WireCanon.ch  ->  Prims.int = (fun ( c  :  WireCanon.ch ) -> (match (c) with
| WireCanon.CQuote -> begin
     (Prims.parse_int "34")
     end
| WireCanon.CBackslash -> begin
     (Prims.parse_int "92")
     end
| WireCanon.CLBrace -> begin
     (Prims.parse_int "123")
     end
| WireCanon.CRBrace -> begin
     (Prims.parse_int "125")
     end
| WireCanon.CLBrack -> begin
     (Prims.parse_int "91")
     end
| WireCanon.CRBrack -> begin
     (Prims.parse_int "93")
     end
| WireCanon.CColon -> begin
     (Prims.parse_int "58")
     end
| WireCanon.CComma -> begin
     (Prims.parse_int "44")
     end
| WireCanon.CMinus -> begin
     (Prims.parse_int "45")
     end
| WireCanon.CPlus -> begin
     (Prims.parse_int "43")
     end
| WireCanon.CDot -> begin
     (Prims.parse_int "46")
     end
| WireCanon.CUpE -> begin
     (Prims.parse_int "69")
     end
| WireCanon.CHexCh (d) -> begin
     (hexd_rank d)
     end
| WireCanon.CLu -> begin
     (Prims.parse_int "117")
     end
| WireCanon.CCtrl (hi, lo) -> begin
     (( 
if hi then begin
     (Prims.parse_int "16")
     end else begin
     (Prims.parse_int "0")
     end) + (hexd_val lo))
     end
| WireCanon.CPlain (uu___) -> begin
     (Prims.parse_int "-1")
     end))


let cmp_ch : WireCanon.ch  ->  WireCanon.ch  ->  Prims.int = (fun ( a  :  WireCanon.ch ) ( b  :  WireCanon.ch ) ->  
if (Prims.op_Equals a b) then begin
     (Prims.parse_int "0")
     end else begin
      
if ((ch_rank a) < (ch_rank b)) then begin
     (Prims.parse_int "-1")
     end else begin
      
if ((ch_rank a) > (ch_rank b)) then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "0")
     end
     end
     end)


let rec cmp_text : Prims.list<WireCanon.ch>  ->  Prims.list<WireCanon.ch>  ->  Prims.int = (fun ( a  :  Prims.list<WireCanon.ch> ) ( b  :  Prims.list<WireCanon.ch> ) -> (match (((a), (b))) with
| ([], []) -> begin
     (Prims.parse_int "0")
     end
| ([], uu___) -> begin
     (Prims.parse_int "-1")
     end
| (uu___, []) -> begin
     (Prims.parse_int "1")
     end
| ((x)::xt, (y)::yt) -> begin
     (

let c = (cmp_ch x y)
in  
if (Prims.op_Less_Greater c (Prims.parse_int "0")) then begin
     c
     end else begin
     (cmp_text xt yt)
     end)
     end))


let rec take = (fun ( k  :  Prims.nat ) ( l  :  Prims.list<'a> ) ->  
if (Prims.op_Equals k (Prims.parse_int "0")) then begin
     []
     end else begin
     (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (x)::(take (k - (Prims.parse_int "1")) t)
     end)
     end)


let rec drop = (fun ( k  :  Prims.nat ) ( l  :  Prims.list<'a> ) ->  
if (Prims.op_Equals k (Prims.parse_int "0")) then begin
     l
     end else begin
     (match (l) with
| [] -> begin
     []
     end
| (uu___)::t -> begin
     (drop (k - (Prims.parse_int "1")) t)
     end)
     end)


let fraction_digits : Prims.list<WireCanon.ch>  ->  Prims.list<WireCanon.ch> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match ((drop (Prims.parse_int "19") s)) with
| (WireCanon.CDot)::t -> begin
     (match (t) with
| [] -> begin
     []
     end
| uu___ -> begin
     (take ((len t) - (Prims.parse_int "1")) t)
     end)
     end
| uu___ -> begin
     []
     end))


let compare_instants : Prims.list<WireCanon.ch>  ->  Prims.list<WireCanon.ch>  ->  Prims.int = (fun ( a  :  Prims.list<WireCanon.ch> ) ( b  :  Prims.list<WireCanon.ch> ) -> (match ((((is_canonical_timestamp a)), ((is_canonical_timestamp b)))) with
| (true, true) -> begin
     (

let head = (cmp_text (take (Prims.parse_int "19") a) (take (Prims.parse_int "19") b))
in  
if (Prims.op_Less_Greater head (Prims.parse_int "0")) then begin
     head
     end else begin
     (cmp_text (fraction_digits a) (fraction_digits b))
     end)
     end
| (true, false) -> begin
     (Prims.parse_int "-1")
     end
| (false, true) -> begin
     (Prims.parse_int "1")
     end
| (false, false) -> begin
     (cmp_text a b)
     end))


let cmp_pair : Prims.int  ->  Prims.int  ->  Prims.int  ->  Prims.int  ->  Prims.int = (fun ( s1  :  Prims.int ) ( f1  :  Prims.int ) ( s2  :  Prims.int ) ( f2  :  Prims.int ) ->  
if (s1 < s2) then begin
     (Prims.parse_int "-1")
     end else begin
      
if (s1 > s2) then begin
     (Prims.parse_int "1")
     end else begin
      
if (f1 < f2) then begin
     (Prims.parse_int "-1")
     end else begin
      
if (f1 > f2) then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "0")
     end
     end
     end
     end)


let year_base : Prims.int  ->  Prims.int = (fun ( yoe  :  Prims.int ) -> (((yoe * (Prims.parse_int "365")) + (yoe / (Prims.parse_int "4"))) - (yoe / (Prims.parse_int "100"))))


let shifted_leap : Prims.int  ->  Prims.bool = (fun ( yoe  :  Prims.int ) -> ((Prims.op_Equals (Prims.mod_f (yoe + (Prims.parse_int "1")) (Prims.parse_int "4")) (Prims.parse_int "0")) && ((Prims.op_Less_Greater (Prims.mod_f (yoe + (Prims.parse_int "1")) (Prims.parse_int "100")) (Prims.parse_int "0")) || (Prims.op_Equals (yoe + (Prims.parse_int "1")) (Prims.parse_int "400")))))


let year_len : Prims.int  ->  Prims.int = (fun ( yoe  :  Prims.int ) ->  
if (shifted_leap yoe) then begin
     (Prims.parse_int "366")
     end else begin
     (Prims.parse_int "365")
     end)


let g : Prims.int  ->  Prims.int = (fun ( doe  :  Prims.int ) -> (((doe - (doe / (Prims.parse_int "1460"))) + (doe / (Prims.parse_int "36524"))) - (doe / (Prims.parse_int "146096"))))


let rec year_ends_ok : Prims.nat  ->  Prims.bool = (fun ( k  :  Prims.nat ) ->  
if (Prims.op_Equals k (Prims.parse_int "0")) then begin
     true
     end else begin
     (

let yoe = (k - (Prims.parse_int "1"))
in (((Prims.op_Equals ((g (year_base yoe)) / (Prims.parse_int "365")) yoe) && (Prims.op_Equals ((g (((year_base yoe) + (year_len yoe)) - (Prims.parse_int "1"))) / (Prims.parse_int "365")) yoe)) && (year_ends_ok yoe)))
     end)


let month_start : Prims.int  ->  Prims.int = (fun ( mp  :  Prims.int ) -> ((((Prims.parse_int "153") * mp) + (Prims.parse_int "2")) / (Prims.parse_int "5")))


let month_of_mp : Prims.int  ->  Prims.int = (fun ( mp  :  Prims.int ) ->  
if (mp < (Prims.parse_int "10")) then begin
     (mp + (Prims.parse_int "3"))
     end else begin
     (mp - (Prims.parse_int "9"))
     end)


let mp_of_month : Prims.int  ->  Prims.int = (fun ( m  :  Prims.int ) -> (Prims.mod_f (m + (Prims.parse_int "9")) (Prims.parse_int "12")))


let shifted_year : Prims.int  ->  Prims.int  ->  Prims.int = (fun ( y  :  Prims.int ) ( m  :  Prims.int ) ->  
if (m <= (Prims.parse_int "2")) then begin
     (y - (Prims.parse_int "1"))
     end else begin
     y
     end)


let era_of : Prims.int  ->  Prims.int = (fun ( yp  :  Prims.int ) -> ((yp + (Prims.parse_int "400")) / (Prims.parse_int "400")))


let yoe_of : Prims.int  ->  Prims.int = (fun ( yp  :  Prims.int ) -> ((yp + (Prims.parse_int "400")) - ((era_of yp) * (Prims.parse_int "400"))))


let year_start : Prims.int  ->  Prims.int = (fun ( yp  :  Prims.int ) -> (((((era_of yp) - (Prims.parse_int "1")) * (Prims.parse_int "146097")) + (year_base (yoe_of yp))) - (Prims.parse_int "719468")))


let lex_lt : civil  ->  civil  ->  Prims.bool = (fun ( a  :  civil ) ( b  :  civil ) -> ((a.year < b.year) || ((Prims.op_Equals a.year b.year) && ((a.month < b.month) || ((Prims.op_Equals a.month b.month) && (a.day < b.day))))))


let rec all_dec : Prims.list<WireCanon.ch>  ->  Prims.bool = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     true
     end
| (c)::t -> begin
     ((match ((dec_val c)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_dec t))
     end))


let dv : WireCanon.ch  ->  Prims.int = (fun ( c  :  WireCanon.ch ) -> (match ((dec_val c)) with
| FStar_Pervasives_Native.Some (v) -> begin
     v
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end))


let rec dval : Prims.list<WireCanon.ch>  ->  Prims.int  ->  Prims.int = (fun ( s  :  Prims.list<WireCanon.ch> ) ( acc  :  Prims.int ) -> (match (s) with
| [] -> begin
     acc
     end
| (c)::t -> begin
     (dval t ((acc * (Prims.parse_int "10")) + (dv c)))
     end))


let cmp_int : Prims.int  ->  Prims.int  ->  Prims.int = (fun ( a  :  Prims.int ) ( b  :  Prims.int ) ->  
if (a < b) then begin
     (Prims.parse_int "-1")
     end else begin
      
if (a > b) then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "0")
     end
     end)


let rec last_ch : Prims.list<WireCanon.ch>  ->  WireCanon.ch = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     ch_Z
     end
| (c)::[] -> begin
     c
     end
| (uu___)::t -> begin
     (last_ch t)
     end))


let rec init : Prims.list<WireCanon.ch>  ->  Prims.list<WireCanon.ch> = (fun ( s  :  Prims.list<WireCanon.ch> ) -> (match (s) with
| [] -> begin
     []
     end
| (uu___)::[] -> begin
     []
     end
| (c)::t -> begin
     (c)::(init t)
     end))


let frac_digits : time_unit  ->  Prims.int  ->  Prims.list<WireCanon.ch> = (fun ( u  :  time_unit ) ( fraction  :  Prims.int ) ->  
if (Prims.op_Equals fraction (Prims.parse_int "0")) then begin
     []
     end else begin
     (trim_zeros (pad (unit_digits u) fraction))
     end)


let prefix19 : civil  ->  Prims.int  ->  Prims.list<WireCanon.ch> = (fun ( c  :  civil ) ( sod  :  Prims.int ) -> (WireCanon.app (civil_text c) ((ch_T)::(WireCanon.app (pad (Prims.parse_int "2") (sod / (Prims.parse_int "3600"))) ((WireCanon.CColon)::(WireCanon.app (pad (Prims.parse_int "2") ((Prims.mod_f sod (Prims.parse_int "3600")) / (Prims.parse_int "60"))) ((WireCanon.CColon)::(pad (Prims.parse_int "2") (Prims.mod_f sod (Prims.parse_int "60"))))))))))

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


let twin_date : Prims.list<WireCanon.ch> = (WireCanon.CHexCh (WireCanon.HD2))::(WireCanon.CHexCh (WireCanon.HD0))::(WireCanon.CHexCh (WireCanon.HD2))::(WireCanon.CHexCh (WireCanon.HD6))::(WireCanon.CMinus)::(WireCanon.CHexCh (WireCanon.HD0))::(WireCanon.CHexCh (WireCanon.HD2))::(WireCanon.CMinus)::(WireCanon.CHexCh (WireCanon.HD2))::(WireCanon.CHexCh (WireCanon.HD8))::[]


let twin_instant : Prims.list<WireCanon.ch> = (WireCanon.app twin_date ((ch_T)::(WireCanon.CHexCh (WireCanon.HD1))::(WireCanon.CHexCh (WireCanon.HD2))::(WireCanon.CColon)::(WireCanon.CHexCh (WireCanon.HD3))::(WireCanon.CHexCh (WireCanon.HD4))::(WireCanon.CColon)::(WireCanon.CHexCh (WireCanon.HD5))::(WireCanon.CHexCh (WireCanon.HD6))::(WireCanon.CDot)::(WireCanon.CHexCh (WireCanon.HD5))::(ch_Z)::[]))


let twins : Prims.list<twin> = ({tname = "days-of-civil-and-civil-of-days-invert-at-the-epoch"; tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (days_of_civil (Prims.parse_int "1970") (Prims.parse_int "1") (Prims.parse_int "1")) (Prims.parse_int "0")) && (Prims.op_Equals (civil_of_days (Prims.parse_int "0")) {year = (Prims.parse_int "1970"); month = (Prims.parse_int "1"); day = (Prims.parse_int "1")})))})::({tname = "date-text-reads-back"; tholds = (fun ( uu___  :  unit ) -> ((Prims.op_Equals (try_days twin_date) (FStar_Pervasives_Native.Some ((Prims.parse_int "20512")))) && (Prims.op_Equals (date_text (Prims.parse_int "20512")) twin_date)))})::({tname = "instant-text-reads-back-in-every-unit-at-least-as-fine"; tholds = (fun ( uu___  :  unit ) -> ((((((Prims.op_Equals (try_instant Milliseconds twin_instant) (FStar_Pervasives_Native.Some ((((Prims.parse_int "1772282096")), ((Prims.parse_int "500")))))) && (Prims.op_Equals (try_instant Nanoseconds twin_instant) (FStar_Pervasives_Native.Some ((((Prims.parse_int "1772282096")), ((Prims.parse_int "500000000"))))))) && (Prims.op_Equals (try_instant Seconds twin_instant) FStar_Pervasives_Native.None)) && (Prims.op_Equals (instant_text Milliseconds (Prims.parse_int "1772282096") (Prims.parse_int "500")) twin_instant)) && (Prims.op_Equals (instant_text Nanoseconds (Prims.parse_int "1772282096") (Prims.parse_int "500000000")) twin_instant)) && (Prims.op_Equals (unit_of twin_instant) Milliseconds)))})::({tname = "compare-instants-is-chronological-not-ordinal"; tholds = (fun ( uu___  :  unit ) -> (((Prims.op_Equals (compare_instants twin_instant (instant_text Milliseconds (Prims.parse_int "1772282096") (Prims.parse_int "0"))) (Prims.parse_int "1")) && (Prims.op_Equals (compare_instants (instant_text Milliseconds (Prims.parse_int "1772282096") (Prims.parse_int "250")) twin_instant) (Prims.parse_int "-1"))) && (Prims.op_Equals (compare_instants twin_instant twin_instant) (Prims.parse_int "0"))))})::({tname = "pad-and-trim"; tholds = (fun ( uu___  :  unit ) -> ((((Prims.op_Equals (pad (Prims.parse_int "3") (Prims.parse_int "7")) ((WireCanon.CHexCh (WireCanon.HD0))::(WireCanon.CHexCh (WireCanon.HD0))::(WireCanon.CHexCh (WireCanon.HD7))::[])) && (Prims.op_Equals (pad (Prims.parse_int "2") (Prims.parse_int "123")) ((WireCanon.CHexCh (WireCanon.HD1))::(WireCanon.CHexCh (WireCanon.HD2))::(WireCanon.CHexCh (WireCanon.HD3))::[]))) && (Prims.op_Equals (pad (Prims.parse_int "4") (Prims.parse_int "-1")) ((WireCanon.CHexCh (WireCanon.HD0))::(WireCanon.CHexCh (WireCanon.HD0))::(WireCanon.CMinus)::(WireCanon.CHexCh (WireCanon.HD1))::[]))) && (Prims.op_Equals (trim_zeros ((WireCanon.CHexCh (WireCanon.HD5))::(WireCanon.CHexCh (WireCanon.HD0))::(WireCanon.CHexCh (WireCanon.HD0))::[])) ((WireCanon.CHexCh (WireCanon.HD5))::[]))))})::[]




