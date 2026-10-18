(* ======================================================================================
   Twin — the twin record every extracted model's fixtures are written in (Phase 504).

   TWINS (Phase 309) are the extractor premise, sampled. The leg's extraction diff makes "the
   oracle is the model" a checked claim about TEXT. Nothing in it says the F# the extractor emits
   COMPUTES what a model means: a mis-extraction that compiles would pass every other step. Each
   fixture applies a model's own functions to a concrete input and compares the result with the
   value the model means there, and the model's closing assertion,
   `assert_norm (twins_hold twins == true)`, is discharged by NORMALISATION — F*'s normaliser
   evaluates every closure to `true` under the model's own semantics. The list is extracted with
   the rest of the model, and the `Proofs.Oracle` family runs the extracted closures against the
   extracted oracle ("twin evaluation"): a closure that comes back `false` there is the F# backend
   disagreeing with the normaliser on that input. Sampled, never proved: the discharge holds on
   these inputs, which is where the `tested` rows already live. The kit's TWIN step
   (`kit/check-proof-leg.ps1`, step 2c) refuses an extracted model that declares no twins.

   WHY ONE MODULE. Until Phase 504 every extracted model carried its own copy of the record and
   of `twins_hold` (23 copies, one per model, DagPrimitives reading DagFold's). The fixtures stay
   in the model they sample; only the record and its evaluator live here. This module is opened BY
   every extracted model and references none, which is `oracle/Prims.fs`'s position: the module
   cone (`check.ps1 -Since`) classifies it as a SHARED input of the leg, so it is inert while it
   does not change and puts every module in when it does (DECISIONS.md D102, as amended).

   It is extracted like a model and carries twins of its own, so the kit holds it to the same
   TWIN step as everything that opens it.
   ====================================================================================== *)
module Twin

noeq type twin = { tname : string; tholds : unit -> bool }

(* F#: `List.forall (fun t -> t.tholds ())`, written as the recursion the normaliser unfolds. *)
let rec twins_hold (l:list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r

let twin_true : twin = { tname = "true"; tholds = (fun () -> true) }
let twin_false : twin = { tname = "false"; tholds = (fun () -> false) }

let twins : list twin = [
  { tname = "twins-hold-of-no-twins";
    tholds = (fun () -> twins_hold []) };
  { tname = "twins-hold-of-holding-twins";
    tholds = (fun () -> twins_hold [ twin_true; twin_true ]) };
  { tname = "twins-hold-refuses-one-failing-twin";
    tholds = (fun () -> not (twins_hold [ twin_true; twin_false ])) } ]

let _ = assert_norm (twins_hold twins == true)
