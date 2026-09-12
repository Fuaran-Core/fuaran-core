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
