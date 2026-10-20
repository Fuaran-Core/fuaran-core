/// The ONE spelling of how this repository's generated artefacts are written (Phase 532): the tool
/// project's command line. Every remedy a held projection prints, and every record that quotes the
/// regenerating command (`copies.json`, `version-derives.json`, CONTRIBUTING.md), is rendered from
/// here, so a move of the tool is one edit.
module Fuaran.Core.Tests.Tool

/// The project the tool is run from, repository-relative.
[<Literal>]
let Project = "tools/Fuaran.Core.Tools"

/// The command prefix, ending before the verb.
let command = sprintf "dotnet run --project %s --" Project

/// The command that renders one registry family (`render <family>`).
let render (family: string) : string = sprintf "%s render %s" command family
