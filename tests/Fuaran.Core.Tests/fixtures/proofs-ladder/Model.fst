module Model

/// A FIXTURE, not a model. It exists so the Phase 144 claims-ladder family (`Proofs.Ladder` in
/// ../../ProofsLadderTests.fs) can be exercised against a ladder that cites nothing in `proofs/`
/// — so the go-red cases stay green while a sibling adds a real model, a real theorem or a real
/// row. It is NOT in `proofs/check.ps1`'s `$modules`, is never verified, never extracted, and
/// nothing links it into a build.
///
/// The three declarations below are the three top-level forms the theorem-exists clause accepts:
/// `val`, `let` and `let rec`, each at column 0.

val alpha (n: nat) : Lemma (n + 0 == n)

let beta (n: nat) : Lemma (0 + n == n) = ()

let rec gamma (n: nat) : Lemma (n - n == 0) =
  if n = 0 then () else gamma (n - 1)

/// Not a top-level declaration: indented, so the clause must NOT accept a row naming it.
let delta_holder () : unit =
  let delta_indented = () in
  delta_indented
