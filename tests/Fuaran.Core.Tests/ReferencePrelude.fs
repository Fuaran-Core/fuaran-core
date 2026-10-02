/// Phase 303 — the HOST PRELUDE the reference vocabulary's support document names
/// (`ReferenceIdl.support.HostPrelude`: module `Fuaran.Core.Tests.ReferencePrelude`, path
/// `ReferencePrelude.fs`). The generated module calls what a domain supplies beside it: the
/// hosted `series` slot's codec (`encSeries` / `decSeries`, a `float list` carried as a JSON
/// array of numbers) and the two readers the support document's verbatim splices use (`jprop`,
/// `jstr`). `AutoOpen`, so a module declared in this namespace sees them unqualified — the one
/// thing a hand-written prelude has to do for the generated layer to compile.
[<AutoOpen>]
module Fuaran.Core.Tests.ReferencePrelude

open Fuaran.Core

/// A float of the hosted series, under WIRE_FORMAT §7 — the same spelling the generated
/// layer's own float slots use, so the hosted slot cannot be the one place a non-finite
/// value is spelled differently.
let private seriesFloat (f: float) : JVal =
    match JVal.nonFiniteToken f with
    | Some token -> JStr token
    | None -> JFloat f

let encSeries (xs: float list) : JVal = JArr(xs |> List.map seriesFloat)

let decSeries (j: JVal) : Result<float list, string> =
    match j with
    | JArr items ->
        (Ok [], List.rev items)
        ||> List.fold (fun acc item ->
            acc
            |> Result.bind (fun tail ->
                match item with
                | JFloat f -> Ok(f :: tail)
                | JInt i -> Ok(float i :: tail)
                | JStr "NaN" -> Ok(nan :: tail)
                | JStr "Infinity" -> Ok(infinity :: tail)
                | JStr "-Infinity" -> Ok(-infinity :: tail)
                | _ -> Error "series: expected a number"))
    | _ -> Error "series: expected an array of numbers"

/// The member `name` of an object, or a refusal naming it.
let jprop (name: string) (j: JVal) : Result<JVal, string> =
    match j with
    | JObj fields ->
        match fields |> List.tryFind (fun (k, _) -> k = name) with
        | Some(_, v) -> Ok v
        | None -> Error("missing member '" + name + "'")
    | _ -> Error "expected an object"

/// A string value, or a refusal.
let jstr (j: JVal) : Result<string, string> =
    match j with
    | JStr s -> Ok s
    | _ -> Error "expected a string"
