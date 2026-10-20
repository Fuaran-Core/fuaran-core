namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The stability classifier's HOST-STRAND join: the roster a manifest declares, and the
/// `WIRE_FORMAT.md` §11 obligations each classified change places on it.
module internal DiffObligations =

    /// Read the roster from a parsed `manifest.json` when it carries one; `None`
    /// when it does not, and the report then obliges no host by name.
    let rosterFrom (manifest: JVal) : Host list option =
        match Decoder.tryMember "hosts" manifest with
        | Some(JArr entries) when not entries.IsEmpty ->
            entries
            |> List.choose (fun e ->
                match (Decoder.optField "id" Decoder.str e |> Result.defaultValue None) with
                | Some id ->
                    Some
                        { Id = id
                          Language =
                            (Decoder.optField "language" Decoder.str e |> Result.defaultValue None)
                            |> Option.defaultValue "?"
                          Role =
                            match (Decoder.optField "role" Decoder.str e |> Result.defaultValue None) with
                            | Some "render-projection" -> RenderProjection
                            | _ -> CodecHost }
                | None -> None)
            |> function
                | [] -> None
                | hs -> Some hs
        | _ -> None

    /// Whether a change touches the wire at all. A host-surface-only change
    /// obliges the reference host's recompile and nothing else in the roster.
    let private touchesWire (c: Classification) =
        match c.Severity with
        | HostSurfaceOnly -> false
        | _ -> true

    /// The §11 family a change reaches, off the descriptor table.
    let private familyOf (c: Change) = (DiffRules.ruleOf c).Family

    /// Does this change alter the NodeKind set? That is the one class that
    /// reaches the authoring veneers, the analyzer vocabulary, the native render
    /// arms and `manifest.kinds`.
    let private isKindSetChange (c: Change) = familyOf c = DiffRules.KindSet

    /// Does it alter a `$type` discriminator family OTHER than NodeKind
    /// (`FormFieldKind`, `ChartKind`, `Binding`, `Action`, `TreeOp` …)?
    let private isFamilyChange (c: Change) =
        familyOf c = DiffRules.DiscriminatorFamily

    let private isEnumSetChange (c: Change) = familyOf c = DiffRules.EnumSet

    /// The obligation set for one change, joined against the roster.
    ///
    /// The two rows worth reading rather than skimming are the veneer rows and
    /// the native render-arm row, because both are conditional and both have
    /// been got wrong in a downstream consumer before:
    ///
    /// - Phase 801 recorded that a payload-FIELD addition binds neither the C#
    ///   `Coverage` reflection nor the VB analyzer's `Vocabulary.cs`, because
    ///   both pin `NodeKind`. §11 step 6 nonetheless speaks of "attribute rows",
    ///   so a field change is `Check`, not `NotBound` — the two authorities do
    ///   not quite agree and the phase author is the one who can settle it.
    /// - Swift's `switch` with no `default:` and Kotlin's `when` over a sealed
    ///   type are exhaustiveness ERRORS, so a case added to a family those tiers
    ///   model is a compiler-forced arm (the 745 precedent, restated by 864) —
    ///   but only for the families they actually model, which the artifact does
    ///   not record.
    ///
    /// **Which rows a roster earns (Phase 252).** Every row naming a corpus, a spec
    /// document, a veneer or an analyzer is the UI tier's §11 checklist, and is emitted
    /// only when the roster declares that tier's reference codec host (`fuaran`). A
    /// roster without it — another vocabulary's, or the EMPTY roster a vocabulary with
    /// no manifest `hosts` gets — is obliged by the rows that hold for any vocabulary:
    /// each declared host, the generated layer, the artifact, and the profile and
    /// emitter rows a breaking verdict carries.
    let obligations (roster: Host list) (c: Classification) : Obligation list =
        let codecHosts = roster |> List.filter (fun h -> h.Role = CodecHost)
        let projections = roster |> List.filter (fun h -> h.Role = RenderProjection)
        let uiTier = roster |> List.exists (fun h -> h.Id = "fuaran")
        let ch = c.Change

        if not (touchesWire c) then
            [ { Surface = "reference host (F#) regeneration"
                Strength = Strength.Required
                Note =
                  "host-surface only — regenerate the generated layer and recompile. No codec host, corpus fixture or spec row is obliged." } ]
        elif not uiTier then
            [ if roster.IsEmpty then
                  { Surface = "hosts: none declared"
                    Strength = Strength.Check
                    Note =
                      "the vocabulary's manifest declares no `hosts`, so no codec host is obliged by name. Every host generated from this vocabulary (`Gen.fsharpModule`, `Gen.typescriptModule`, `Gen.jsonSchema`) regenerates in the same change-set; declare `hosts` in the manifest to have each named here." }

              for h in codecHosts do
                  { Surface = sprintf "codec: %s (%s)" h.Id h.Language
                    Strength = Strength.Required
                    Note = "encoder + decoder + schema shape, same change-set, pinned to the vocabulary's documents." }

              for h in projections do
                  { Surface = sprintf "render arm: %s (%s)" h.Id h.Language
                    Strength =
                      (if isKindSetChange ch then
                           Strength.Required
                       else
                           Strength.Check)
                    Note =
                      "a projection that models the changed family as a closed type gains or loses an arm; one that does not is unaffected." }

              { Surface = "generated layer: regenerate"
                Strength = Strength.Required
                Note = "regenerate every generated module and schema from the new vocabulary, and recompile." }

              { Surface = "artifact: idl.json"
                Strength = Strength.Required
                Note =
                  "re-render the vocabulary artifact beside the vocabulary it projects; a regenerate-and-byte-compare guard fails when the committed artifact and a fresh emission disagree." }

              if c.Severity = BreakingWire then
                  { Surface = "profile: §15 negotiation"
                    Strength = Strength.Required
                    Note =
                      "a major wire event moves the `/vN/` segment; an older consumer must classify the new profile `Foreign` and hard-refuse it (STABILITY.md §15 negotiate outcomes)." }

              if c.Severity = BreakingForEmitters then
                  { Surface = "downstream emitters"
                    Strength = Strength.Required
                    Note =
                      "coordinate the bump with every emitter, and advance the producing package's `<Version>` in the SAME commit — an unmoved version re-packs the slot under consumers already pinned to it." } ]
        else
            [ for h in codecHosts do
                  if h.Id = "fuaran" then
                      { Surface = "codec: fuaran (F#, reference)"
                        Strength = Strength.Required
                        Note =
                          "IDL + `--regen-snapshots` + `sync-generated-layer.ps1`, then the policy decoder (`JsonDecode.fs`) and `SchemaGen.fs` — §11 steps 1-3." }
                  else
                      { Surface = sprintf "codec: %s (%s)" h.Id h.Language
                        Strength = Strength.Required
                        Note =
                          "encoder + decoder + schema shape, same change-set — §11 step 5; pinned to the corpus by its §11.1 leg." }

              for h in projections do
                  { Surface = sprintf "render arm: %s (%s)" h.Id h.Language
                    Strength =
                      (if isKindSetChange ch then
                           Strength.Required
                       else
                           Strength.Check)
                    Note =
                      if isKindSetChange ch then
                          "a NodeKind lacking an arm is a BUILD error in the native tier (§11.0 render projections). No codec change — the Rust core owns the codec."
                      else
                          "bound only if this tier models the changed family as a sealed type — Swift's `switch` without `default:` and Kotlin's `when` are exhaustiveness errors, so a modelled family forces an arm (the 745 precedent). Do not soften either host's default-deny to make a suite pass." }

              { Surface = "corpus: wire-format-fixtures fixture"
                Strength = Strength.Required
                Note =
                  "§11 step 4 — `--emit-corpus`, and run `Fuaran.UI.Tests` in the same session (the corpus-as-a-set assertions live only there). The corpus is its own repo: commit and PUSH it with the codec commits." }

              { Surface = "schema: schema.json"
                Strength = Strength.Required
                Note = "regenerated by the same `--emit-corpus` command; the stale-schema guard fails if it is skipped." }

              { Surface = "artifact: idl.json"
                Strength = Strength.Required
                Note =
                  "re-render the vocabulary artifact beside the vocabulary it projects; the domain's regenerate-and-byte-compare guard fails when the committed artifact and a fresh emission disagree." }

              if isKindSetChange ch then
                  { Surface = "veneer: C# fluent factory (Fuaran.UI.CSharp)"
                    Strength = Strength.Required
                    Note =
                      "§11 step 6 — a factory + options record for the kind. The coverage-vs-corpus test fires the moment step 4's fixture lands." }

                  { Surface = "veneer: VB XML-literal mapping (Fuaran.UI.VisualBasic)"
                    Strength = Strength.Required
                    Note = "§11 step 6 — an element registration driving that factory." }

                  { Surface = "analyzer: VB Vocabulary.cs"
                    Strength = Strength.Required
                    Note =
                      "§11 step 6 — the kind name + attribute rows. Mind the pin's blind spot: a kind missing from BOTH the translator and the analyzer keeps the vocabulary-pin test green." }

                  { Surface = "manifest: manifest.kinds"
                    Strength = Strength.Required
                    Note = "the machine-readable kind enumeration (§11.2) — regenerated with the corpus." }

                  { Surface = "spec: WIRE_FORMAT.md §3.2 kind table"
                    Strength = Strength.Required
                    Note = "the kind's row + its spec-record shape." }
              else
                  { Surface = "veneers + analyzer (C#/VB)"
                    Strength = Strength.Check
                    Note =
                      "Phase 801 recorded that a payload-FIELD addition binds neither the C# `Coverage` reflection nor the VB analyzer's `Vocabulary.cs` (both pin `NodeKind`); §11 step 6 nonetheless names \"attribute rows\". Settle it for this change rather than inheriting either reading." }

              if isFamilyChange ch then
                  { Surface = "spec: WIRE_FORMAT.md §11 discriminator-family list"
                    Strength = Strength.Check
                    Note =
                      "§11 enumerates the families the rule is stated over. A change that introduces a family adds a row; a change within an existing one does not." }

              if isEnumSetChange ch then
                  { Surface = "spec: WIRE_FORMAT.md closed-set enumeration"
                    Strength = Strength.Required
                    Note = "the closed set's admitted strings are normative doc text as well as schema `enum` array." }

              if c.Severity = BreakingWire then
                  { Surface = "profile: §15 negotiation"
                    Strength = Strength.Required
                    Note =
                      "a major wire event moves the `/vN/` segment; an older consumer must classify the new profile `Foreign` and hard-refuse it (STABILITY.md §15 negotiate outcomes)." }

              if c.Severity = BreakingForEmitters then
                  { Surface = "downstream emitters"
                    Strength = Strength.Required
                    Note =
                      "coordinate the bump with every emitter, and advance the producing package's `<Version>` in the SAME commit — an unmoved version re-packs the slot under consumers already pinned to it." } ]
