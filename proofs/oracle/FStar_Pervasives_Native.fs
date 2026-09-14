// The second half of the runtime the F*-extracted models beside this file compile against —
// Phase 131's finding 2, met again by a model that needed one more library type.
//
// `Prims.fs` closes the gap for the names the F# backend emits from `Prims` (the list type, the
// string and bool types, decidable equality, string concatenation). A model that uses F*'s
// `option` — `TreeOps.fst` does, because `Tree.tryFind` and `Tree.parentOf` return one and the
// model is faithful to them — makes the backend emit `FStar_Pervasives_Native.option` /
// `.Some` / `.None`, and the release ships no F# implementation of THAT module either.
//
// Two lines close it. Nothing here is semantics: the type is F#'s own option under F*'s spelling,
// and the constructor names are the ones the extractor writes.
//
// It is hand-written and therefore NOT diffed by the proof leg, exactly as `Prims.fs` is not —
// the leg holds the GENERATED files to a fresh extraction, and these two are the floor they stand
// on. Adding a model that reaches for another library type means adding to this floor; the
// alternative, restating `option` inside each model so the extraction depends on `Prims` alone,
// buys a shorter shim at the cost of a model that no longer reads like the F# it is about.
module FStar_Pervasives_Native

type option<'a> =
    | None
    | Some of 'a
