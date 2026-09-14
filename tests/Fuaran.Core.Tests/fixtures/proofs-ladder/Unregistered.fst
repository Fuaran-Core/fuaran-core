module Unregistered

/// A FIXTURE, as `Model.fst` beside it is. It exists so the model-registered go-red can cite a
/// model file that EXISTS but is absent from the module list — which is the drift the clause is
/// about (a row claiming a model the proof leg never checks), separated from the different drift
/// of a row citing a path that is not there at all.

let omega (n: nat) : Lemma (n * 1 == n) = ()
