namespace Fuaran.Core.Tests


open Fuaran.Core

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
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "stringEscape"
                  "description", CorpusFile.str description
                  "format", CorpusFile.str "name<TAB>spelling<TAB>pre-image"
                  // A line carries no member, so the stamp is the file's, for every line (Phase 478).
                  "answeredBy", CorpusFile.answeredBy AnsweredBy.Reference ]
              Items = "lines"
              Rows = StringEscapeVectors.lines () |> List.map CorpusFile.str }

    let path (dir: string) =
        System.IO.Path.Combine(dir, familyDirName, fileName)
