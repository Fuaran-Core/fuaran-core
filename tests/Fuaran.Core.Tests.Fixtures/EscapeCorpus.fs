namespace Fuaran.Core.Tests


open Fuaran.Core

/// The index of a line family (Phase 568): `escape/` and `encoding/` each carry one table, and each
/// directory's own `manifest.json` records it in the one manifest schema with the adoption record
/// (D222), so a host's measurement of a table is written where a host reads it. A table's vectors are
/// its lines. The reference's entry is this repository's suite, which holds every line to the code
/// (`StringEscapeTests`, `EncodingProfileTests`); the wire hosts read `proposed`; each spine's entry is
/// the one table of spines' (`SpineAdoption`), whose declines are stated in the row's description.
module LineFamilyManifest =

    /// The index over one line table.
    let internal manifest (familyId: string) (fileName: string) (lines: int) (summary: string) : CorpusManifest =
        { Version = 1
          Description =
            "A Core-owned line table (Phase 568). SELF-ENUMERATED, as apply/ is: not indexed by the corpus root manifest.json. `vectors` counts the table's lines, each one vector. `adoption` is per host and is DATA: a host whose entry reads `proposed` reports the family BY NAME rather than skipping it, and flips its own entry in the change-set that lands its leg. This manifest is authoritative for the line count."
          Families =
            [ { Id = familyId
                Kind = "line-vectors"
                File = fileName
                Vectors = lines
                Adoption =
                  ("fuaran", HostAdoption.Measured(lines, AdoptionSource.Here))
                  :: (ApplyVectorExport.hostRoster |> List.map (fun h -> h, HostAdoption.Proposed))
                  @ SpineAdoption.adoptions familyId
                Description = String.concat " " (summary :: SpineAdoption.declineNotes familyId) } ] }

    let manifestPath (familyDirName: string) (dir: string) =
        System.IO.Path.Combine(dir, familyDirName, "manifest.json")

/// The committed rendering of `StringEscapeVectors.lines` (Phase 349): the file a host in another
/// language diffs its own rendering against, so the line FORMAT is pinned in the repository rather
/// than recomputed by whoever reads it. Written by `render escape`, held to a fresh render below.
module EscapeCorpus =

    let familyDirName = "escape"
    let fileName = "string-escape.json"

    let private description =
        "String-escape vectors (Phase 287; committed since Phase 349). One rule spells a string inside canonical JSON: a quotation mark as backslash-quote, a backslash as two, every control character U+0000-U+001F as a lower-case backslash-u escape with no short form, and nothing else. Each entry of `lines` is one line of the table, three fields separated by one TAB (U+0009), no field carrying a TAB or a line break: for each character vector (every control character, the quotation mark, the backslash, then a plain-text control that must pass through unescaped) its name, its canonical spelling (the body of the JSON string literal, quotes excluded) and the actor encoding of a human whose id is that character; then for each named actor its name, its encoding and the linear chain pre-image at sequence 0 with an empty op. A host that reproduces this package's chain hashes renders the same lines, byte for byte and in this order."

    /// The file, rendered: a header, then one JSON string per line of the table.
    let render () : string =
        let lines = StringEscapeVectors.lines ()

        // Every line as the escaper's model answers it, in table order (`ChainOracle`).
        let model: VectorOracle.Answer option =
            Some
                { Model = ChainOracle.EscapeModel
                  LadderRow = ChainOracle.EscapeRow
                  Cell =
                    CorpusCell.List(
                        [ for v in StringEscapeVectors.vectors -> ChainOracle.characterLine v.Name v.Input
                          for name, actor, _ in StringEscapeVectors.actorVectors -> ChainOracle.actorLine name actor ]
                        |> List.map CorpusFile.str
                    ) }

        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "stringEscape"
                  "description", CorpusFile.str description
                  "format", CorpusFile.str "name<TAB>spelling<TAB>pre-image"
                  // A line carries no member, so the stamp is the file's, for every line (Phase 478): the
                  // model answers the whole table or the file is the reference's (Phase 563).
                  VectorOracle.stamp "string-escape" (CorpusCell.List(lines |> List.map CorpusFile.str)) model ]
              Items = "lines"
              Rows = lines |> List.map CorpusFile.str }

    let path (dir: string) =
        System.IO.Path.Combine(dir, familyDirName, fileName)

    /// The family's index (Phase 568).
    let renderManifest () : string =
        LineFamilyManifest.manifest
            "stringEscape"
            fileName
            (List.length (StringEscapeVectors.lines ()))
            "The one string spelling inside canonical JSON: each character's canonical spelling and actor encoding, then each named actor's encoding and chain pre-image."
        |> CorpusFile.manifest
        |> CorpusFile.render

    let manifestPath (dir: string) =
        LineFamilyManifest.manifestPath familyDirName dir
