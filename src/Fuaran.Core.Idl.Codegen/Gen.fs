namespace Fuaran.Core.Idl

open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit

/// The source generators over an `Idl`: the F# structural layer (`fsharpTypes`,
/// `fsharpModuleWith`), the JSON schema, the TypeScript encoder and declarations, and the
/// scaffold values — each a source string a consumer compiles or publishes, refused as a
/// typed `CodegenError` wherever the vocabulary asks for something the backend cannot emit.
///
/// Phase 293 — a FACADE: every entry point is a one-line forward to the `internal` emitter
/// modules under `Emit/`, each over the one `Decl` resolution. The types it takes and answers
/// (`GenSupport`, `KindProjection`, `Derivation`) are declared in `Fuaran.Core.Idl.Codegen.Types`
/// since Phase 524, ahead of every emitter, so no emitter reads a mirror of them.
module Gen =

    /// Which declarations are generic in `'Msg` — see `Decl.msgCarrying`.
    let internal msgCarrying (idl: Idl) : Set<string> = Decl.msgCarrying idl

    /// The F# type declarations for a whole vocabulary, from the one type emitter (Phase 293).
    let fsharpTypes (idl: Idl) : Result<string, CodegenError> =
        Decl.declared idl
        |> Decl.laidOut
        |> Result.bind Decl.unparameterised
        |> Result.bind FSharpTypes.fsharpTypes

    /// Emit a compiling, self-contained F# encoder module (`moduleName`) for the named kinds,
    /// drawing in the enums/unions they transitively reference, under the declared support.
    /// The emitted text is LF-terminated whatever the generator was built from.
    let fsharpModuleWith
        (sup: GenSupport)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        Decl.resolve idl sup.KindProjections kindTags
        |> Decl.laidOut
        |> Result.bind Decl.unparameterised
        |> Result.bind (FSharpCodec.fsharpModuleWith sup moduleName)

    /// `fsharpModuleWith` plus the requested structural derivations (Phase 374) and, under
    /// `Derivation.SpecDecoders`, the collecting decoders and public per-spec entries (Phase 377),
    /// appended after the module's existing members. A `Derivation.KindTable` reads the support's
    /// `KindTables` (Phase 689). The empty list emits exactly what `fsharpModuleWith` does.
    let fsharpModuleDerived
        (sup: GenSupport)
        (derivations: Derivation list)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        Decl.resolve idl sup.KindProjections kindTags
        |> Decl.laidOut
        |> Result.bind Decl.unparameterised
        |> Result.bind (FSharpCodec.fsharpModuleDerived sup derivations moduleName)

    /// The pre-945 entry — `fsharpModuleWith` under an empty declared-support record,
    /// emitting byte-identically to the generator before the support channel existed.
    let fsharpModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        fsharpModuleWith GenSupport.Empty moduleName idl kindTags

    /// Phase 550 — a CODEC-ONLY F# module over host-declared types (see [[CodecOnly]]): an abbreviation
    /// of each reached declaration's host type, the encoder and decoder groups the structural module
    /// prints, a strict-members walker beside each decoder, and per root the public `encode<Name>`,
    /// `decode<Name>` and `decode<Name>Strict`. Refused by name for a root the vocabulary does not
    /// declare, a reached declaration with no host type, a reached node, and type-declaring support.
    let fsharpCodecModule
        (sup: GenSupport)
        (request: CodecOnly)
        (moduleName: string)
        (idl: Idl)
        : Result<string, CodegenError> =
        Decl.unparameterisedIdl idl
        |> Result.bind (fun () -> FSharpCodecOnly.fsharpCodecModule sup request moduleName idl)

    /// Phase 685 — `fsharpCodecModule` with a per-root visibility option: each root `internalWriters`
    /// names emits its `encode<Name>` `internal`, so a host type's writer stays inside its package
    /// while its `decode<Name>` and `decode<Name>Strict` are published. Every other entry is public,
    /// and an empty list emits exactly what `fsharpCodecModule` emits. Refused by name for a name that
    /// is not a root.
    let fsharpCodecModuleWith
        (sup: GenSupport)
        (request: CodecOnly)
        (internalWriters: string list)
        (moduleName: string)
        (idl: Idl)
        : Result<string, CodegenError> =
        Decl.unparameterisedIdl idl
        |> Result.bind (fun () -> FSharpCodecOnly.fsharpCodecModuleWith sup request internalWriters moduleName idl)

    /// The JSON Schema (draft 2020-12) of the vocabulary's wire.
    let jsonSchema (idl: Idl) : Result<string, CodegenError> =
        Decl.declared idl |> Decl.laidOut |> Result.bind JsonSchema.jsonSchema

    /// Phase 644 — the JSON Schema (draft 2020-12) of the arguments of every method of every
    /// contract, in the shape a tool definition takes: one object schema a method, keyed
    /// `<contract>.<method>`, contracts and methods in declared order. Empty for a vocabulary that
    /// declares no contract. Nothing a runtime needs is emitted: no client, server table,
    /// protocol, route, envelope or tool executor.
    let contractArgumentSchemas (idl: Idl) : Result<(string * string) list, CodegenError> =
        Decl.declared idl
        |> Decl.laidOut
        |> Result.bind (fun decl ->
            (Ok [], idl.Contracts |> List.collect (fun c -> c.Methods |> List.map (fun m -> c, m)))
            ||> List.fold (fun acc (c, m) ->
                match acc, JsonSchema.methodArguments decl c m with
                | Error e, _
                | _, Error e -> Error e
                | Ok done_, Ok schema -> Ok(done_ @ [ c.Name + "." + m.Id, Fuaran.Core.Json.render schema ])))

    /// Phase 644 — the JSON Schema of one method's arguments, named `<contract>.<method>`; a name
    /// the vocabulary does not declare is refused.
    let methodArgumentsSchema (idl: Idl) (method: string) : Result<string, CodegenError> =
        contractArgumentSchemas idl
        |> Result.bind (fun all ->
            match all |> List.tryFind (fun (name, _) -> name = method) with
            | Some(_, schema) -> Ok schema
            | None ->
                Error(
                    CodegenError.UnsupportedConstruct(
                        sprintf "the contract method `%s`" method,
                        "the vocabulary declares no such method",
                        "name a method as `<contract>.<method>`"
                    )
                ))

    /// A TypeScript value literal for an IDL value under the given wire shape.
    let internal typescriptValueWith (shape: WireShape) (v: IdlValue) : string =
        TypeScriptCodec.typescriptValueWith shape v

    /// Phase 539 (DECISIONS.md D190) — the TypeScript codec reads each member under its one name and
    /// applies one presence condition, [[WhenNonEmpty]] (Phase 660, D256), so it REFUSES a vocabulary
    /// that declares an alias or any other presence condition, naming the first, rather than emitting a
    /// codec that would read the canonical form differently from the F# host. The declarations leg is
    /// unaffected: neither shape moves a type.
    let private tsCodecAdmits (idl: Idl) : Result<unit, CodegenError> =
        match Decl.canonicalFormDeclarationsBeyond TypeScriptCodec.applies idl with
        | [] -> Ok()
        | what :: _ ->
            Error(
                CodegenError.UnsupportedConstruct(
                    what,
                    "GP5: a backend applies a canonical-form declaration or refuses it by name, never ignores it",
                    "generate this vocabulary's codec with the F# backend, which applies it"
                )
            )

    /// Phase 689 (DECISIONS.md D278) — the per-kind table lookup is emitted by the F# backend only, so the
    /// TypeScript backend refuses a `Derivation.KindTable` request by name rather than export nothing for it.
    let private tsDerives (derivations: Derivation list) : Result<unit, CodegenError> =
        match
            derivations
            |> List.tryPick (fun d ->
                match d with
                | Derivation.KindTable n ->
                    Some(
                        CodegenError.UnsupportedConstruct(
                            sprintf "the kind table `%s`" n,
                            "GP5: a backend applies a derivation or refuses it by name, never ignores it",
                            "emit the table's lookup with the F# backend (`fsharpModuleDerived`)"
                        )
                    )
                | _ -> None)
        with
        | None -> Ok()
        | Some e -> Error e

    /// The TypeScript structural encoder/decoder module for the named kinds.
    let typescriptModule (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        tsCodecAdmits idl
        |> Result.bind (fun () -> Decl.laidOut (Decl.resolve idl Map.empty kindTags))
        |> Result.bind Decl.unparameterised
        |> Result.bind TypeScriptCodec.typescriptModule

    /// Phase 380 — `typescriptModule` plus the requested derivations, the TypeScript host's
    /// `fsharpModuleDerived`: the same requests, appended after the module's members with one
    /// further `export` naming them. The empty list emits exactly what `typescriptModule` does. A
    /// request is admitted by the one derivation admission the F# path performs, over this host's
    /// resolution — which declares no host projections, because a projection is F# source — and
    /// refused with that admission's error (Phase 511). `MapMsg` emits nothing here: this host holds a handler slot as its sentinel's `null`
    /// and carries no message type, so there is nothing to map.
    let typescriptModuleDerived
        (derivations: Derivation list)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        tsDerives derivations
        |> Result.bind (fun () -> tsCodecAdmits idl)
        |> Result.bind (fun () -> Decl.laidOut (Decl.resolve idl Map.empty kindTags))
        |> Result.bind Decl.unparameterised
        |> Result.bind (TypeScriptDerived.typescriptModuleDerived derivations)

    /// A TypeScript value literal for an authored value of a declared type.
    let typescriptValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        Decl.unparameterisedIdl idl
        |> Result.bind (fun () -> TypeScriptDeclarations.typescriptValue idl t v)

    /// The TypeScript type declarations for the named kinds.
    let typescriptDeclarations (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        Decl.resolve idl Map.empty kindTags
        |> Decl.laidOut
        |> Result.bind Decl.unparameterised
        |> Result.bind TypeScriptDeclarations.typescriptDeclarations

    /// Phase 381 — `typescriptDeclarations` plus one declaration per member
    /// `typescriptModuleDerived` exports under the same requests, so the derived module and its
    /// declaration file agree: every exported name is declared and every declared name exported.
    /// The empty list emits exactly what `typescriptDeclarations` does; a request is admitted and
    /// refused exactly as `typescriptModuleDerived` admits and refuses it. `MapMsg` declares
    /// nothing, as the module exports nothing for it, and the file's header comment says why.
    let typescriptDeclarationsDerived
        (derivations: Derivation list)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        tsDerives derivations
        |> Result.bind (fun () -> Decl.laidOut (Decl.resolve idl Map.empty kindTags))
        |> Result.bind Decl.unparameterised
        |> Result.bind (TypeScriptDerived.typescriptDeclarationsDerived derivations)

    /// An authored value as compilable F# source — the scaffold leg: an expression of type
    /// `Result<'T, DecodeError>` since Phase 384, a hosted slot's codec refusal its `Error`.
    let fsharpValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        Decl.unparameterisedIdl idl
        |> Result.bind (fun () -> Scaffold.fsharpValue idl t v)

    /// The provenance header every scaffold leg emits.
    let provenanceHeader (commentPrefix: string) (sourceWireHash: string) (actor: string) : string =
        Scaffold.provenanceHeader commentPrefix sourceWireHash actor
