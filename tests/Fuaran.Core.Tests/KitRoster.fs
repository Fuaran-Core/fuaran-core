/// Phase 257 — the law-family roster as the SUITE reads it: both packages' shares, composed.
///
/// The kit's families ship from two assemblies since Phase 257 (DECISIONS.md D68): the families
/// that read the dataframe layer from `Fuaran.Core.DataFrame.Conformance`, every other one from
/// `Fuaran.Core.Conformance`. Each package declares its own share (`Families.roster`,
/// `DataFrameFamilies.roster`), because the kit cannot name the other package's families without
/// the upward reference the boundary test refuses. This suite references both, so it is the one
/// place the whole roster is visible, and every check that used to read `Families.families` or
/// `SampleAdequacy.census` reads the composition here instead.
module Fuaran.Core.Tests.KitRoster

open System.Reflection
open Fuaran.Core

/// Both shares, the kit's first. The order is the order the generated docs name the packages in.
let rosters: Families.Roster list = [ Families.roster; DataFrameFamilies.roster ]

/// Every law family, across both packages.
let families: Families.LawFamily list = rosters |> List.collect _.Families

/// The composed roster's keys, sorted.
let ids: string list = families |> List.map _.Id |> List.sort

/// The modules the composed roster covers, sorted.
let modules: string list =
    families |> List.map _.Module |> List.distinct |> List.sort

/// The family with this id, from whichever package ships it.
let tryFind (id: string) : Families.LawFamily option =
    families |> List.tryFind (fun f -> f.Id = id)

/// The package that ships the family with this id.
let packageOf (id: string) : string option =
    rosters
    |> List.tryFind (fun r -> r.Families |> List.exists (fun f -> f.Id = id))
    |> Option.map _.Package

/// Every refusal-audit row, across both packages.
let refusalAudit: Families.RefusalAudit list =
    rosters |> List.collect _.RefusalAudit

/// The audit row for one family.
let tryRefusal (id: string) : Families.RefusalAudit option =
    refusalAudit |> List.tryFind (fun a -> a.Family = id)

/// Every adequacy-census row, across both packages.
let census: (string * AdequacyClass) list = rosters |> List.collect _.Census

/// Every ladder obligation the composed roster discharges, sorted by obligation.
let obligations: (string * string) list =
    [ for f in families do
          for o in f.Discharges -> o, f.Id ]
    |> List.sortBy fst

/// The two assemblies the kit ships from, the kit's first. The second is loaded by name: an F#
/// module has no `typeof`, and every type it declares a family over belongs to the kit.
let assemblies: Assembly list =
    [ typeof<LawResult>.Assembly
      Assembly.Load(AssemblyName "Fuaran.Core.DataFrame.Conformance") ]

/// The adequacy and refusal cells, over the composed roster.
let adequacyToken (cases: (string * CaseCount) list) (id: string) : string =
    Families.adequacyTokenOf rosters cases id

let refusalToken (id: string) : string = Families.refusalTokenOf rosters id

/// The generated renderings, over the composed roster.
let toMarkdownWith (cases: (string * CaseCount) list) : string = Families.toMarkdownOf rosters cases

let toJsonWith (cases: (string * CaseCount) list) : string = Families.toJsonOf rosters cases

let toMarkdown () : string = toMarkdownWith []
