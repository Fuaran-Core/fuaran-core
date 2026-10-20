/// The law-family roster as the SUITE reads it: the share THIS repository ships.
///
/// Phase 257 split the kit's families across two assemblies (DECISIONS.md D68): the families that
/// read the dataframe layer went to `Fuaran.Core.DataFrame.Conformance`, every other one stayed in
/// `Fuaran.Core.Conformance`, and each package declares its own share (`Families.roster`,
/// `DataFrameFamilies.roster`). Phase 258 moved the dataframe share out of this repository with the
/// rest of the compute strand (D66), so the composition is one share again. It stays a list, and
/// every roster, census and audit check still reads it here, so the checks quantify over exactly the
/// families this repository is answerable for; the dataframe share is certified by the compute
/// repository's own suite.
module Fuaran.Core.Tests.KitRoster

open System.Reflection
open Fuaran.Core

/// This repository's share.
let rosters: Families.Roster list = [ Families.roster ]

/// Every law family in this share.
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

/// Every refusal-audit row in this share.
let refusalAudit: Families.RefusalAudit list =
    rosters |> List.collect _.RefusalAudit

/// The audit row for one family.
let tryRefusal (id: string) : Families.RefusalAudit option =
    refusalAudit |> List.tryFind (fun a -> a.Family = id)

/// Every adequacy-census row in this share.
let census: (string * AdequacyClass) list = rosters |> List.collect _.Census

/// Every ladder obligation the composed roster discharges, sorted by obligation.
let obligations: (string * string) list =
    [ for f in families do
          for o in f.Discharges -> o, f.Id ]
    |> List.sortBy fst

/// The assembly this share ships from.
let assemblies: Assembly list = [ typeof<LawResult>.Assembly ]

/// The adequacy and refusal cells, over the composed roster.
let adequacyToken (cases: (string * CaseCount) list) (id: string) : string =
    Families.adequacyTokenOf rosters cases id

let refusalToken (id: string) : string = Families.refusalTokenOf rosters id

/// The generated renderings, over the composed roster.
let toMarkdownWith (cases: (string * CaseCount) list) : string = Families.toMarkdownOf rosters cases

let toJsonWith (cases: (string * CaseCount) list) : string = Families.toJsonOf rosters cases

let toMarkdown () : string = toMarkdownWith []
