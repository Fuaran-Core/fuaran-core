module LegCustard

(* The kit's own fixture for the custard leg (Phase 641): a first-order model with a record and a
   theorem, small enough to check in a second. Extracted in one Custard run beside LegPulse. *)

type pair = { first : int; second : int }

let rec sum (xs : list int) : int =
  match xs with
  | [] -> 0
  | x :: rest -> x + sum rest

let rec append (xs ys : list int) : list int =
  match xs with
  | [] -> ys
  | x :: rest -> x :: append rest ys

let swap (p : pair) : pair = { first = p.second; second = p.first }

let rec sum_append (xs ys : list int)
  : Lemma (ensures sum (append xs ys) == sum xs + sum ys) =
  match xs with
  | [] -> ()
  | _ :: rest -> sum_append rest ys
