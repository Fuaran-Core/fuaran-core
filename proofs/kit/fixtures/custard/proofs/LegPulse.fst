module LegPulse
#lang-pulse

(* The kit's own Pulse fixture (Phase 641): a heap cell in a Pulse box, proved to be bumped twice.
   It checks with the flags every model uses (the release carries Pulse on its own include path),
   and Custard extracts the box as an F# ref cell, which the fixture's hosts run on .NET and Node. *)

open Pulse.Lib.Pervasives
module B = Pulse.Lib.Box
open Pulse.Lib.Box { box, (:=), (!) }

fn bump (r : box int) (#v : erased int)
  requires B.pts_to r v
  ensures B.pts_to r (v + 1)
{
  let x = !r;
  r := x + 1;
}

fn add_two (n : int)
  requires emp
  returns m : int
  ensures pure (m == n + 2)
{
  let r = B.alloc n;
  bump r;
  bump r;
  let m = !r;
  B.free r;
  m
}
