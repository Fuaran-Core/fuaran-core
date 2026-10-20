module Fuaran.Core.Tests.SpecDecodersIdl

open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 377 — the collecting decoders over the support channel. The reference vocabulary under
// its declared support document exercises every channel a collecting decoder must route around:
// a hosted slot (a leaf, lifted), a case REFINE (`Text.Lookup`), a host PROJECTION (`Note`), a
// verbatim decoder splice, and — declared here, and only here — a TRANSPARENT case (`Text.Inline`
// encodes bare), so the bare arm of a collecting union decoder is compiled too.
//
// Phase 460 — and a PLACEHOLDER THAT NAMES A LOCAL: the parameterised union `Binding` carries a
// `TFn` slot whose placeholder calls the type-parameter decoder `decT`, the name the generator
// documents as reachable from a placeholder in BOTH emitters. Until 460 the collecting emitter
// bound only `colT`, so this module did not compile.
//
// `SpecDecodersGenerated.fs` is the F# emitted from it with `Gen.Derivation.SpecDecoders`
// requested, committed and checked for drift by `IdlSpecDecoderTests`. The derivations
// vocabulary (`DeriveIdl`) requests the decoders as well; that module carries the field-shape
// laws, this one the support channel.
// ---------------------------------------------------------------------------

let private field name t opt : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

/// `Binding<'T>` — a case whose placeholder reaches the type-parameter decoder by its documented
/// name, `decT`, beside a member of the parameter's own type.
let private binding: IdlUnion =
    { Name = "Binding"
      Params = [ "T" ]
      Cases =
        [ { Tag = "Local"
            Fields =
              [ field
                    "parse"
                    (TFn
                        { FSharp = "JVal -> 'T option"
                          TypeScript = "(v: unknown) => T | undefined"
                          Placeholder = "(fun (v: JVal) -> decT v |> Result.toOption)" })
                    Required
                field "value" (TVar "T") Required ]
            Annotations = Annotations.Empty }
          { Tag = "Ref"
            Fields = [ field "name" TStr Required ]
            Annotations = Annotations.Empty } ]
      Annotations = Annotations.Empty }

/// The kind that carries a `Binding<int>`, so the placeholder's arm is reached from a public entry.
let private bind: IdlKind =
    { Tag = "Bind"
      Category = "content"
      Annotations = Annotations.Empty
      Fields = [ field "binding" (TUnion("Binding", [ TInt ])) Required ] }

/// The reference vocabulary, `Text.Inline` declared transparent, with `Bind` / `Binding` added
/// (each sorts first, so the vocabulary stays in canonical order).
let idl: Idl =
    { ReferenceIdl.refIdl with
        Kinds = bind :: ReferenceIdl.refIdl.Kinds
        Unions = binding :: ReferenceIdl.refIdl.Unions
        Harden =
            { ReferenceIdl.refIdl.Harden with
                TransparentUnions = [ "Text", "Inline" ] } }

/// The module name the committed generated file declares.
[<Literal>]
let moduleName = "Fuaran.Core.Tests.SpecDecodersGenerated"

/// The committed generated file, repository-relative.
[<Literal>]
let generatedFile = "tests/Fuaran.Core.Tests/SpecDecodersGenerated.fs"

/// The module text the generator emits for this vocabulary with the decoders requested.
let generate () : Result<string, CodegenError> =
    Gen.fsharpModuleDerived
        ReferenceIdl.support.Support
        [ Gen.Derivation.SpecDecoders ]
        moduleName
        idl
        (idl.Kinds |> List.map _.Tag)
