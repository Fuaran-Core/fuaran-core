/// The law-family roster as the SUITE reads it: the share THIS repository ships.
///
/// Phase 257 split the kit's families across two assemblies (DECISIONS.md D68), and Phase 258 moved
/// the dataframe share out of this repository with the rest of the compute strand (D66), so this
/// repository ships ONE roster, `Families.roster`; the dataframe share is certified by the compute
/// repository's own suite. Every roster, census and audit check reads it here, so the checks
/// quantify over exactly the families this repository is answerable for. It was a one-element list
/// until Phase 537; a composition of rosters returns with a second share, not before.
module Fuaran.Core.Tests.KitRoster

open System.Reflection
open Fuaran.Core

/// This repository's share.
let roster: Families.Roster = Families.roster

/// Every law family in this share.
let families: Families.LawFamily list = roster.Families

/// The roster's keys, sorted.
let ids: string list = families |> List.map _.Id |> List.sort

/// The modules the roster covers, sorted.
let modules: string list =
    families |> List.map _.Module |> List.distinct |> List.sort

/// The family with this id.
let tryFind (id: string) : Families.LawFamily option =
    families |> List.tryFind (fun f -> f.Id = id)

/// The package that ships the family with this id.
let packageOf (id: string) : string option =
    tryFind id |> Option.map (fun _ -> roster.Package)

/// Every refusal-audit row in this share.
let refusalAudit: Families.RefusalAudit list = roster.RefusalAudit

/// The audit row for one family.
let tryRefusal (id: string) : Families.RefusalAudit option =
    refusalAudit |> List.tryFind (fun a -> a.Family = id)

/// Every adequacy-census row in this share.
let census: (string * AdequacyClass) list = roster.Census

/// Every ladder obligation the roster discharges, sorted by obligation.
let obligations: (string * string) list =
    [ for f in families do
          for o in f.Discharges -> o, f.Id ]
    |> List.sortBy fst

/// The assembly this share ships from.
let assemblies: Assembly list = [ typeof<LawResult>.Assembly ]

/// The adequacy and refusal cells, over the roster.
let adequacyToken (cases: (string * CaseCount) list) (id: string) : string = Families.adequacyToken cases id

let refusalToken (id: string) : string = Families.refusalToken id

/// The generated renderings, over the roster.
let toMarkdownWith (cases: (string * CaseCount) list) : string = Families.toMarkdownWith cases

let toJsonWith (cases: (string * CaseCount) list) : string = Families.toJsonWith cases

let toMarkdown () : string = toMarkdownWith []
