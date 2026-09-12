module Fuaran.Core.Tests.IdlCodegenEolTests

open Expecto
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 129 — the emitted artefact's line endings belong to the GENERATOR.
//
// Two inputs could put a carriage return into generated source, and a consumer
// regenerating from the packaged generator has no way to see either of them:
//
//  1. The generator's own multi-line `"""…"""` templates bake whatever line
//     ending `Codegen.fs` had on disk in the checkout that compiled it. The
//     repository pins LF (`.gitattributes`), but a formatter run rewrites the
//     working copy to the platform's own ending, so a package built after one
//     emits CRLF where the published package emits LF — same version, same
//     declaration, different bytes, and the consumer's regeneration guard then
//     reports drift with nothing to point at.
//
//  2. Declared support (`GenSupport`) is authored data: doc blocks and verbatim
//     splices that travel as a `support.json` written on whatever machine wrote
//     it, and annotation prose carried in the IDL itself.
//
// Neither was visible to the two committed-artefact drift guards in this suite
// when the bake happened: both compared through a whitespace-STRIPPING
// normaliser, so an emission differing only in line endings was equal to them.
// That is why the bake reached a downstream consumer before anything here
// noticed. Those guards are byte-for-byte since D30, so a CR in a committed
// artefact is now caught there too — but they catch it only where an artefact is
// COMMITTED, and only after a regeneration has been run. The tests below assert
// the property at the generator's own boundary, over emissions no committed
// artefact covers (authored support, annotation prose, the TypeScript backend),
// which is where a consumer regenerating from the packaged generator meets it.
//
// Each test below asserts BOTH halves, and the second is what keeps the first
// from being vacuous: the artefact carries no CR, AND the probe text is present
// in its LF form — so a generator that silently dropped or escaped the authored
// prose fails here rather than passing by emitting nothing.
// ---------------------------------------------------------------------------

/// Authored prose carrying a CRLF in the middle of it.
let private crProbe = "phase-129 probe line one\r\nphase-129 probe line two"

/// The same prose as it must appear in the emitted artefact.
let private lfProbe = "phase-129 probe line one\nphase-129 probe line two"

let private miniIdl = Fuaran.Core.Idl.Spike.Fixtures.miniIdl

/// The slice the spike's generated modules cover — the same list `IdlSpikeTests`
/// and the regeneration entry point use.
let private kinds =
    [ "Heading"; "Badge"; "Button"; "Metric"; "Box"; "Markdown"; "Tabs" ]

/// `miniIdl` with `TextSource.Literal` marked deprecated, its free-prose message
/// carrying the CRLF. Annotation prose reaches BOTH backends' comments verbatim,
/// which makes one fixture serve the F# and TypeScript legs alike.
let private annotatedIdl: Idl =
    let annotate (c: IdlUnionCase) =
        if c.Tag <> "Literal" then
            c
        else
            { c with
                Annotations =
                    { Annotations.Empty with
                        Deprecated =
                            Some
                                { Replacement = None
                                  Message = Some crProbe } } }

    { miniIdl with
        Unions =
            miniIdl.Unions
            |> List.map (fun u ->
                if u.Name <> "TextSource" then
                    u
                else
                    { u with
                        Cases = u.Cases |> List.map annotate }) }

/// Declared support carrying the CRLF on both of its verbatim channels — a doc
/// block attached to a declaration, and a splice appended to the type group.
let private crSupport =
    { Gen.GenSupport.Empty with
        Docs = Map.ofList [ "type:HeadingVariant", [ "/// " + crProbe ] ]
        TypeSplice = Some("// " + crProbe) }

let private expectLfOnly (what: string) (emitted: string) =
    Expect.isFalse (emitted.Contains "\r") (what + " carries no carriage return")
    Expect.stringContains emitted lfProbe (what + " carries the authored prose, LF-terminated")

[<Tests>]
let tests =
    testList
        "IDL codegen — emitted line endings"
        [

          test "fsharpTypes: authored prose carrying a CR emits an LF artefact" {
              expectLfOnly "the emitted F# type declarations" (Gen.fsharpTypes annotatedIdl)
          }

          test "fsharpModuleWith: declared support carrying a CR emits an LF artefact" {
              match Gen.fsharpModuleWith crSupport "Phase129.Probe" miniIdl kinds with
              | Error e -> failtestf "codegen rejected the spike vocabulary: %A" e
              | Ok emitted -> expectLfOnly "the emitted F# module" emitted
          }

          test "typescriptModule: authored prose carrying a CR emits an LF artefact" {
              match Gen.typescriptModule annotatedIdl kinds with
              | Error e -> failtestf "the TypeScript backend rejected the spike vocabulary: %A" e
              | Ok emitted -> expectLfOnly "the emitted TypeScript module" emitted
          }

          // The three above go red on ANY checkout with the normalisation removed (measured).
          // This one cannot: with no authored CR to carry, an LF checkout emits LF whether the
          // generator normalises or not. It is a regression pin for the template-baking half —
          // the case that only bites on a generator built from a CRLF working copy — and it is
          // recorded here as such rather than counted as certification.
          test "an unannotated emission carries no carriage return either" {
              match Gen.fsharpModule "Phase129.Plain" miniIdl kinds, Gen.typescriptModule miniIdl kinds with
              | Ok fs, Ok ts ->
                  Expect.isFalse (fs.Contains "\r") "the emitted F# module carries no carriage return"
                  Expect.isFalse (ts.Contains "\r") "the emitted TypeScript module carries no carriage return"
              | Error e, _
              | _, Error e -> failtestf "codegen rejected the spike vocabulary: %A" e
          } ]
