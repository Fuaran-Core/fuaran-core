// The runtime the F*-extracted `DagFold.fs` beside this file compiles against.
//
// F*'s F# backend (`--codegen FSharp`) emits code that names a handful of `Prims` members —
// the list type, the string and bool types, and decidable equality — and the F* release ships
// no F# implementation of them (the OCaml backend has one; the F# one is second-class, which is
// one of the findings the Phase 131 spike recorded). These are the aliases that close the gap.
// Nothing here is semantics: every name is the F# primitive under an F* spelling.
module Prims

type list<'a> = Microsoft.FSharp.Collections.List<'a>
type string = System.String
type bool = System.Boolean

/// F*'s `=` on an `eqtype` — decidable structural equality.
let inline op_Equals (x: 'a) (y: 'a) : bool = (x = y)

/// F*'s `^` on strings — `Prims.strcat`. Named by the Phase 135 extraction, which reproduces
/// `Decode`'s error MESSAGES rather than merely their class, so the differential compares what
/// a failure says as well as that it failed.
let inline strcat (x: string) (y: string) : string = x + y

/// F*'s `<>` on an `eqtype` — decidable structural DISequality, the counterpart of `op_Equals`
/// above. Named by the Phase 146 extraction, whose error-kind confinement predicates are written
/// as "every kind but this one". Like everything else here it is the F# primitive under an F*
/// spelling; there is no semantics in this file.
let inline op_Less_Greater (x: 'a) (y: 'a) : bool = (x <> y)
