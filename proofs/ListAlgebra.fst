(* ======================================================================================
   ListAlgebra — the Prims-only list algebra every model opens (Phase 504).

   NAMED `ListAlgebra`, not `Prelude`: the pinned prover's library ships `FStar.Prelude`, and F*'s
   implicit `FStar.` namespace shadows a module of that name in every file that names it (Warning
   274, measured on the first run of this phase), so `Prelude` would be one resolution rule away
   from meaning the library's module.

   `app`, `mem`, `len`, `rev`, `distinct`, `inter` and `diff`, defined here rather than taken from
   `FStar.List.Tot` so that the extracted oracle depends on `Prims` alone (the oracle-independence
   guard, `kit/check-proof-leg.ps1` section 4b). Until Phase 504 each model defined the ones it
   used, and the same seven functions stood in twelve models under the same names, some of them
   specialised to `string`; a proof about one copy was a proof about that copy only. They are one
   definition each now, and every model that uses them opens this module.

   WHAT IS NOT HERE, AND WHY.
   - The accumulator reverse (`WireDecode.rev_app` / `rev`, `JsonParse.rev_app` / `rev`,
     `Unit.rev_acc` / `rev`) is a different function from `rev` below, with its own proofs; the
     generated vocabularies (`Fuaran.Core.Idl.Codegen`'s F* target) read WireDecode's by name, so
     it stays where the generator expects it.
   - `DagFold.lookup` (a node by id, answering `found`) and `TreeDiff.lookup` (an association list
     read last-binding-first) are two different functions that share a name; neither is a copy.
   - No lemma about these functions lives here. Each model proves what it needs about them, by the
     names it always used, so moving the definitions moved no statement.

   THE CONE. This module is opened BY every model that uses the algebra and references no model,
   which is `oracle/Prims.fs`'s position: the module cone (`check.ps1 -Since`) classifies it as a
   SHARED input of the leg — inert while it does not change, every module in when it does
   (DECISIONS.md D102, as amended). It opens `Twin`, the other shared module, for its own twins:
   it is extracted like a model, so the kit's TWIN step holds it to sampling the algebra on the
   F# it extracts to.
   ====================================================================================== *)
module ListAlgebra

open Twin

(* F#: `l @ m`. *)
let rec app (#a:Type0) (l m:list a) : Tot (list a) =
  match l with
  | [] -> m
  | x :: t -> x :: app t m

(* F#: `List.contains`. *)
let rec mem (#a:eqtype) (x:a) (l:list a) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem x t

(* F#: `List.length`. *)
let rec len (#a:Type0) (l:list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: `List.rev`, as the specification the proofs unfold. *)
let rec rev (#a:Type0) (l:list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> app (rev t) [x]

(* F#: `List.distinct xs = xs`. *)
let rec distinct (#a:eqtype) (l:list a) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> not (mem x t) && distinct t

(* F#: `Set.intersect`, keeping the left list's order. *)
let rec inter (#a:eqtype) (x y:list a) : Tot (list a) =
  match x with
  | [] -> []
  | h :: t -> if mem h y then h :: inter t y else inter t y

(* F#: `Set.difference`, keeping the left list's order. *)
let rec diff (#a:eqtype) (x y:list a) : Tot (list a) =
  match x with
  | [] -> []
  | h :: t -> if mem h y then diff t y else h :: diff t y

(* TWINS (Phase 309) — the algebra, sampled on the F# it extracts to. See `Twin`. *)
let twins : list twin = [
  { tname = "app-appends";
    tholds = (fun () -> app [ "a"; "b" ] [ "c" ] = [ "a"; "b"; "c" ] && app [] [ "c" ] = [ "c" ]) };
  { tname = "mem-finds-a-member-and-only-a-member";
    tholds = (fun () -> mem "b" [ "a"; "b" ] && not (mem "c" [ "a"; "b" ]) && not (mem "a" [])) };
  { tname = "len-counts";
    tholds = (fun () -> len [ "a"; "b"; "c" ] = 3 && len #string [] = 0) };
  { tname = "rev-reverses";
    tholds = (fun () -> rev [ "a"; "b"; "c" ] = [ "c"; "b"; "a" ]) };
  { tname = "distinct-refuses-a-repeat";
    tholds = (fun () -> distinct [ "a"; "b"; "c" ] && not (distinct [ "a"; "b"; "a" ])) };
  { tname = "inter-keeps-the-left-order";
    tholds = (fun () -> inter [ "a"; "b"; "c" ] [ "c"; "a" ] = [ "a"; "c" ]) };
  { tname = "diff-keeps-the-left-order";
    tholds = (fun () -> diff [ "a"; "b"; "c" ] [ "b" ] = [ "a"; "c" ]) } ]

let _ = assert_norm (twins_hold twins == true)
