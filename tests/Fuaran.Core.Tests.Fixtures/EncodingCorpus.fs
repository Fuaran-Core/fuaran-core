namespace Fuaran.Core.Tests


open Fuaran.Core

/// The committed rendering of `EncodingProfileVectors.lines`: the table a host in another language,
/// or a later rewrite of the live renderer, diffs against. Written by `render encoding`, held to a
/// fresh render below.
module EncodingCorpus =

    let familyDirName = "encoding"
    let fileName = "encoding-profiles.json"

    let private description =
        "Encoding-profile vectors (Phase 360). A content-addressed store names the rendering its ids were computed under: v1 is the rendering of 0.30.0 through 0.32.0, which spells line feed, carriage return and tab as the short escapes; v2 is the rendering since 0.33.0, which spells every control character U+0000-U+001F as a lower-case backslash-u escape. Each entry of `lines` is one line of the table, three fields separated by one TAB (U+0009), no field carrying a TAB or a line break: for each value its name, its v1 rendering and its v2 rendering (the value is what either rendering parses to); then for each actor `actor: <name>`, its v1 and its v2 encoding as folded into a chain payload and a DAG node id; then the capture pre-image (effect read<TAB>line, determinism io<LF>wall, value \"v\") under each. The v1 column was measured against the published 0.30.0 binaries. A host that reproduces a store's ids under a declared profile renders the same column, byte for byte."

    /// The file, rendered: a header, then one JSON string per line of the table.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "encodingProfile"
                  "description", CorpusFile.str description
                  "format", CorpusFile.str "name<TAB>v1<TAB>v2"
                  // A line carries no member, so the stamp is the file's, for every line (Phase 478).
                  "answeredBy", CorpusFile.answeredBy AnsweredBy.Reference ]
              Items = "lines"
              Rows = EncodingProfileVectors.lines () |> List.map CorpusFile.str }

    let path (dir: string) =
        System.IO.Path.Combine(dir, familyDirName, fileName)
