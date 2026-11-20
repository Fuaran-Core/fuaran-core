namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core

/// Phase 293 — the single landing site for declared annotations in generated F#: the `///`
/// block for a reader and the `Obsolete` attribute for the compiler, and the one statement of
/// the precedence between a support doc block and an authored annotation.
module internal Annotations =

    // -----------------------------------------------------------------------
    // Phase 113 — declared annotations, rendered into the generated F#.
    //
    // Two surfaces, and they answer different questions. The `///` block is for a
    // reader: it says what is true about the member and what to do instead. The
    // `System.Obsolete` attribute is for the compiler: it makes the fact reach a
    // consumer who never opens the generated file.
    // -----------------------------------------------------------------------

    /// A declared string slot that actually says something. An annotation authored
    /// with an empty or whitespace replacement / message / version reads as UNSAID
    /// rather than emitting `use `` instead` into the generated source — refusing
    /// garbage at the point of emission is cheaper than a validator every caller has
    /// to remember to run, and there is nothing else the slot could have meant.
    let said (v: string option) : string option =
        v |> Option.filter (fun x -> not (System.String.IsNullOrWhiteSpace x))

    /// Phase 292 — a kind's CATEGORY as an F# `//` comment, one line per authored line
    /// ([[SourceLit.fsDocLines]]), so a category carrying a line break cannot end the
    /// comment early. An empty category keeps the `// ` it has always emitted.
    let fsCategoryComment (category: string) : string =
        match SourceLit.fsDocLines category with
        | [] -> "// "
        | lines -> lines |> List.map (fun l -> "// " + l) |> String.concat "\n"

    /// The authored lines an annotation set says, unindented: the doc's (Phase 255), then the
    /// notes'. The authored doc comes FIRST — it says what the member is, and the notes after
    /// it say what is true about it — so a block that gains a doc gains exactly its lines and
    /// nothing else moves.
    let private authoredTexts (a: Annotations) : string list * string list =
        let docTexts =
            match a.Doc with
            | Some d -> SourceLit.fsDocLines d
            | None -> []

        let noteTexts =
            [ match a.Deprecated with
              | Some d ->
                  let replacement =
                      match said d.Replacement with
                      | Some r -> sprintf " Use `%s` instead." r
                      | None -> ""

                  "**Deprecated.**" + replacement

                  match said d.Message with
                  | Some m -> m
                  | None -> ()
              | None -> ()

              if a.InProcessOnly then
                  "**In-process only** — this member has no wire projection: a value here"
                  "is carried inside one host process and is LOST across any wire boundary."

              match said a.Since with
              | Some v -> sprintf "Since `%s`." v
              | None -> () ]
            // Phase 292 — the notes ride the doc's line discipline: a deprecation message,
            // replacement or version carrying a line break used to END the `///` comment,
            // and everything after it was live source in the generated module.
            |> List.collect SourceLit.fsDocLines

        docTexts, noteTexts

    let private docLine (indent: string) (text: string) =
        if text = "" then indent + "///" else indent + "/// " + text

    /// The `///` doc lines for an annotation set, at the given indent, when they OPEN the
    /// member's block. Empty for an empty set, which is what keeps an unannotated
    /// vocabulary's emission byte-identical.
    ///
    /// **The block's MODE is the F# compiler's, and the emitter follows it rather than
    /// fighting it.** A `///` block whose first line does not begin with `<` is TEXT:
    /// the compiler wraps it in `<summary>` and XML-encodes it itself, so the authored
    /// prose is emitted verbatim — encoding it here as well would show a reader
    /// `&lt;'T>` where the author wrote `<'T>`. A block whose first line DOES begin
    /// with `<` is XML, taken as written, and authored text there is not valid XML in
    /// general (FS3390). So a doc that opens with `<` is emitted as an explicit
    /// `<summary>` holding every line of the block, the notes included, encoded — the
    /// one case where a block gains lines around it as well as the doc's own.
    let annotationDocLines (indent: string) (a: Annotations) : string list =
        let docTexts, noteTexts = authoredTexts a

        match docTexts with
        | first :: _ when first.TrimStart().StartsWith "<" ->
            (indent + "/// <summary>")
            :: ((docTexts @ noteTexts) |> List.map (SourceLit.fsDocXml >> docLine indent))
            @ [ indent + "/// </summary>" ]
        | _ -> (docTexts @ noteTexts) |> List.map (docLine indent)

    /// The single `System.Obsolete` attribute an annotation set earns, or `None`.
    ///
    /// **One attribute, never two.** `System.ObsoleteAttribute` is not
    /// `AllowMultiple`, so a member that is both deprecated and in-process-only
    /// composes into one message; a second attribute would not compile.
    ///
    /// **`isError = false` is load-bearing.** The generated layer must not decide
    /// for its host that touching a marked member fails the build: FS0044 is a
    /// warning the host escalates (`--warnaserror:44`) or silences (`--nowarn:44`)
    /// on its own release schedule. An unconditional error would make the
    /// two-release retirement this annotation exists to enable impossible to run —
    /// the release that MARKS the member could never ship.
    ///
    /// `Since` earns no attribute: it is a fact about the past, and there is nothing
    /// for a compiler to say about it.
    let obsoleteAttr (a: Annotations) : string option =
        let parts =
            [ match a.Deprecated with
              | Some d ->
                  "deprecated"
                  + (match said d.Replacement with
                     | Some r -> " — use `" + r + "` instead"
                     | None -> "")
                  + (match said d.Message with
                     | Some m -> ": " + m
                     | None -> "")
              | None -> ()

              if a.InProcessOnly then
                  "in-process only — no wire projection; a value here is lost across a wire boundary" ]

        match parts with
        | [] -> None
        | ps -> Some(sprintf "[<System.Obsolete(%s, false)>]" (SourceLit.fsAttribute (String.concat "; " ps)))

    /// Whether ANY declaration in the vocabulary earns an `Obsolete` attribute — the
    /// condition for the generated module's `#nowarn "44"`. The generated structural
    /// layer constructs and matches every declared member, marked ones included, so
    /// the warning it raises is for CONSUMERS of the layer and never for the layer
    /// itself; without the suppression a vocabulary could not mark anything without
    /// making its own generated codec noisy.
    let emitsObsolete (idl: Idl) : bool =
        let fieldSets =
            [ idl.NodeFields
              yield! idl.Kinds |> List.map _.Fields
              yield! idl.Ops |> List.map _.Fields
              yield! idl.Records |> List.map _.Fields
              yield! idl.Unions |> List.collect (fun u -> u.Cases |> List.map _.Fields) ]

        (idl.Unions
         |> List.exists (fun u -> u.Cases |> List.exists (fun c -> (obsoleteAttr c.Annotations).IsSome)))
        // Phase 119 — a kind, a tree-op, and an enum CASE each earn the attribute on the
        // same terms, so each is a reason for the suppression on the same terms too.
        || (idl.Kinds |> List.exists (fun k -> (obsoleteAttr k.Annotations).IsSome))
        || (idl.Ops |> List.exists (fun o -> (obsoleteAttr o.Annotations).IsSome))
        || (idl.Enums
            |> List.exists (fun e -> e.CaseAnnotations |> List.exists (fun (_, a) -> (obsoleteAttr a).IsSome)))
        || (fieldSets
            |> List.exists (List.exists (fun f -> (obsoleteAttr f.Annotations).IsSome)))
        // Phase 542 — a union, record or enum TYPE earns it on the same terms.
        || ([ yield! idl.Unions |> List.map _.Annotations
              yield! idl.Records |> List.map _.Annotations
              yield! idl.Enums |> List.map _.Annotations ]
            |> List.exists (fun a -> (obsoleteAttr a).IsSome))

    // -----------------------------------------------------------------------
    // Phase 293 — THE PRECEDENCE between a declared support doc and an authored annotation,
    // stated once. A declaration's comment block is: the category comment (a kind), then the
    // SUPPORT doc block (`support.json`, `type:<Name>`), then the AUTHORED annotation lines
    // (`Annotations` on the IDL declaration — `deprecated`, `since`, the doc annotation). Support
    // leads because it is the domain's hand-written prose about the declaration as a whole;
    // the annotations follow because each names one fact the compiler also reads (the
    // `Obsolete` attribute). Neither replaces the other, and neither is dropped.
    // -----------------------------------------------------------------------

    /// Phase 713 — the mode a support doc block sets for the `///` block it opens: `Some true`
    /// when its first `///` line opens with `<` (XML), `Some false` when that line is text, and
    /// `None` when the block carries no `///` line and so leaves the mode to the lines after it.
    let private supportMode (supportDoc: string option) : bool option =
        supportDoc
        |> Option.bind (fun block ->
            block.Split '\n'
            |> Array.map _.TrimStart()
            |> Array.tryFind _.StartsWith("///")
            |> Option.map (fun l -> l.Substring(3).TrimStart().StartsWith "<"))

    /// The comment lines above one declaration: the support doc block (if any) ahead of the
    /// authored annotation lines, at the given indent. `supportDoc` is the block as it is
    /// printed (each line at the declaration's indent, no trailing line break).
    ///
    /// **Phase 713 — the support block decides the mode of the whole `///` block**, because it
    /// holds the block's first line. Under a support block in XML mode the authored lines would
    /// be text inside XML, which is not valid XML in general (FS3390, and a tool reading the
    /// comment drops text outside an element): they are emitted as an explicit `<remarks>`
    /// holding every authored line, encoded. Under a support block in text mode they are
    /// verbatim, with no `<summary>` of their own, which the compiler would encode and show as
    /// text. A support block with no `///` line leaves the mode to [[annotationDocLines]].
    let memberDocLines (indent: string) (supportDoc: string option) (a: Annotations) : string list =
        let support = supportDoc |> Option.toList

        match supportMode supportDoc with
        | None -> support @ annotationDocLines indent a
        | Some xml ->
            match authoredTexts a with
            | [], [] -> support
            | docTexts, noteTexts when xml ->
                support
                @ [ indent + "/// <remarks>" ]
                @ ((docTexts @ noteTexts) |> List.map (SourceLit.fsDocXml >> docLine indent))
                @ [ indent + "/// </remarks>" ]
            | docTexts, noteTexts -> support @ ((docTexts @ noteTexts) |> List.map (docLine indent))

    /// The support block `doc` renders at a member's site — each line indented, the block
    /// line-terminated, empty when the member has none — as [[memberDocLines]]' `supportDoc`.
    let supportAt (rendered: string) : string option =
        if rendered = "" then
            None
        else
            Some(rendered.Substring(0, rendered.Length - 1))
